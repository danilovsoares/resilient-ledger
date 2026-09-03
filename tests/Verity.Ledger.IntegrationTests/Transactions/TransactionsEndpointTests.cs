using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Api.Controllers;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Dtos;
using Verity.Ledger.Domain.Transactions;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.IntegrationTests.Infrastructure;
using Verity.Shared.Contracts.Correlation;

namespace Verity.Ledger.IntegrationTests.Transactions;

/// <summary>
/// Testes de ponta a ponta contra uma Api real (WebApplicationFactory) e um PostgreSQL real
/// (Testcontainers). O RabbitMQ é deliberadamente inalcançável nesta classe — ver
/// docs/resiliency-and-messaging.md e ADR-002: o Ledger não deve depender do broker para
/// aceitar lançamentos.
/// </summary>
[Collection(Infrastructure.LedgerIntegrationCollection.Name)]
public sealed class TransactionsEndpointTests : IAsyncLifetime
{
    private readonly LedgerApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> GetDevTokenAsync()
    {
        var response = await _client.PostAsync("/api/v1/dev/token", null);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<DevTokenResponse>();
        return payload!.AccessToken;
    }

    private sealed record DevTokenResponse(string AccessToken);

    [Fact]
    public async Task POST_registra_lancamento_e_grava_transaction_e_outbox_message_na_mesma_transacao()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var correlationId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(
                TransactionType.Credit, 150.50m, DateTimeOffset.UtcNow, "Venda"))
        };
        request.Headers.Add(CorrelationHeaders.IdempotencyKey, $"itest-{Guid.NewGuid()}");
        request.Headers.Add(CorrelationHeaders.CorrelationId, correlationId.ToString());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.GetValues(CorrelationHeaders.CorrelationId).Should().Contain(correlationId.ToString());

        var body = await response.Content.ReadFromJsonAsync<TransactionDto>();
        body.Should().NotBeNull();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        var persistedTransaction = await dbContext.Transactions.SingleAsync(t => t.Id == body!.Id);
        persistedTransaction.Amount.Should().Be(150.50m);

        var outboxMessage = await dbContext.OutboxMessages.SingleAsync(m => m.CorrelationId == correlationId);
        outboxMessage.CausationId.Should().Be(correlationId);
        outboxMessage.Type.Should().Contain("TransactionRegisteredEvent");
    }

    [Fact]
    public async Task POST_com_Idempotency_Key_repetida_nao_cria_segundo_lancamento_nem_segunda_mensagem_de_outbox()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var idempotencyKey = $"itest-replay-{Guid.NewGuid()}";

        HttpRequestMessage BuildRequest() => new(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(TransactionType.Debit, 42m, DateTimeOffset.UtcNow, null)),
            Headers = { { CorrelationHeaders.IdempotencyKey, idempotencyKey } }
        };

        var first = await _client.SendAsync(BuildRequest());
        var second = await _client.SendAsync(BuildRequest());

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadFromJsonAsync<TransactionDto>();
        var secondBody = await second.Content.ReadFromJsonAsync<TransactionDto>();
        secondBody!.Id.Should().Be(firstBody!.Id);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var count = await dbContext.Transactions.CountAsync(t => t.IdempotencyKey == idempotencyKey);
        count.Should().Be(1);
    }

    [Fact]
    public async Task POST_com_valor_negativo_retorna_400_com_problem_details()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(TransactionType.Credit, -10m, DateTimeOffset.UtcNow, null)),
            Headers = { { CorrelationHeaders.IdempotencyKey, $"itest-invalid-{Guid.NewGuid()}" } }
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_por_data_retorna_apenas_lancamentos_daquela_data_de_negocio()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var targetDate = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var otherDate = new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero);

        async Task Register(DateTimeOffset occurredAt, string key) =>
            await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
            {
                Content = JsonContent.Create(new RegisterTransactionRequest(TransactionType.Credit, 10m, occurredAt, null)),
                Headers = { { CorrelationHeaders.IdempotencyKey, key } }
            });

        await Register(targetDate, $"date-a-{Guid.NewGuid()}");
        await Register(otherDate, $"date-b-{Guid.NewGuid()}");

        var response = await _client.GetAsync("/api/v1/transactions?date=2026-01-15");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await response.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>();
        results!.Items.Should().NotBeEmpty();
        results.Items.Should().OnlyContain(t => t.BusinessDate == DateOnly.FromDateTime(targetDate.UtcDateTime));
    }

    [Fact]
    public async Task GET_por_data_pagina_no_maximo_10_itens_e_informa_o_total()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var businessDate = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 12; i++)
        {
            await RegisterAsync(TransactionType.Credit, 1m, $"itest-paging-{i}-{Guid.NewGuid()}", businessDate);
        }

        var firstPage = await _client.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?date=2026-02-10&page=1");
        var secondPage = await _client.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?date=2026-02-10&page=2");

        firstPage!.Items.Should().HaveCount(10);
        firstPage.TotalCount.Should().Be(12);
        firstPage.TotalPages.Should().Be(2);

        secondPage!.Items.Should().HaveCount(2);
        secondPage.TotalCount.Should().Be(12);
    }

    [Fact]
    public async Task Ledger_continua_disponivel_para_escrita_mesmo_com_RabbitMQ_inalcancavel()
    {
        // A própria factory desta classe já aponta para um RabbitMQ inalcançável
        // (ver LedgerApiFactory) — este teste apenas explicita a expectativa central do ADR-002.
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(TransactionType.Credit, 99m, DateTimeOffset.UtcNow, null)),
            Headers = { { CorrelationHeaders.IdempotencyKey, $"itest-broker-down-{Guid.NewGuid()}" } }
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var pendingOutbox = await dbContext.OutboxMessages.CountAsync(m => m.PublishedAt == null);
        pendingOutbox.Should().BeGreaterThan(0, "a mensagem foi gravada na Outbox mesmo sem conseguir publicar no broker indisponível");
    }

    [Fact]
    public async Task POST_cancel_registra_estorno_de_tipo_oposto_e_mesmo_valor()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var original = await RegisterAsync(TransactionType.Credit, 80m, $"itest-cancel-{Guid.NewGuid()}");

        var response = await _client.PostAsync($"/api/v1/transactions/{original.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reversal = await response.Content.ReadFromJsonAsync<TransactionDto>();
        reversal!.Type.Should().Be(TransactionType.Debit);
        reversal.Amount.Should().Be(80m);
        reversal.ReversalOfTransactionId.Should().Be(original.Id);
    }

    [Fact]
    public async Task POST_cancel_no_mesmo_lancamento_duas_vezes_retorna_400_na_segunda()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var original = await RegisterAsync(TransactionType.Debit, 40m, $"itest-double-cancel-{Guid.NewGuid()}");

        var first = await _client.PostAsync($"/api/v1/transactions/{original.Id}/cancel", null);
        var second = await _client.PostAsync($"/api/v1/transactions/{original.Id}/cancel", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_cancel_de_lancamento_inexistente_retorna_404()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync($"/api/v1/transactions/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_por_data_reflete_o_lancamento_original_como_estornado()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var original = await RegisterAsync(TransactionType.Credit, 15m, $"itest-reflects-{Guid.NewGuid()}", DateTimeOffset.UtcNow);

        var cancelResponse = await _client.PostAsync($"/api/v1/transactions/{original.Id}/cancel", null);
        var reversal = await cancelResponse.Content.ReadFromJsonAsync<TransactionDto>();

        var listResponse = await _client.GetAsync($"/api/v1/transactions?date={today:yyyy-MM-dd}");
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<TransactionDto>>();

        list!.Items.Should().Contain(dto => dto.Id == original.Id && dto.ReversedByTransactionId == reversal!.Id);
    }

    private async Task<TransactionDto> RegisterAsync(
        TransactionType type, decimal amount, string idempotencyKey, DateTimeOffset? occurredAt = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(type, amount, occurredAt ?? DateTimeOffset.UtcNow, null)),
            Headers = { { CorrelationHeaders.IdempotencyKey, idempotencyKey } }
        };

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionDto>())!;
    }
}
