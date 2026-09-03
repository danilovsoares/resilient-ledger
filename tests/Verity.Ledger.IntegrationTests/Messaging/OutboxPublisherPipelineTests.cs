using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;
using Verity.Ledger.Api.Controllers;
using Verity.Ledger.Domain.Transactions;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.IntegrationTests.Infrastructure;
using Verity.Shared.Contracts.Correlation;
using ContractTransactionType = Verity.Shared.Contracts.IntegrationEvents.TransactionType;
using TransactionRegisteredEvent = Verity.Shared.Contracts.IntegrationEvents.TransactionRegisteredEvent;

namespace Verity.Ledger.IntegrationTests.Messaging;

/// <summary>
/// Ao contrário de <see cref="Transactions.TransactionsEndpointTests"/> (que roda com o
/// RabbitMQ deliberadamente inalcançável), esta classe aponta a Api para um broker real
/// (Testcontainers.RabbitMq) e prova que o <c>OutboxPublisherService</c> de fato publica a
/// mensagem pendente e marca <c>published_at</c> — não apenas que ela fica gravada na Outbox.
/// </summary>
[Collection(LedgerIntegrationCollection.Name)]
public sealed class OutboxPublisherPipelineTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    private readonly LedgerApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_factory.InitializeAsync(), _rabbitMq.StartAsync());

        _factory.WithReachableRabbitMq(new Uri(_rabbitMq.GetConnectionString()));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _rabbitMq.StopAsync();
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
    public async Task Mensagem_de_outbox_e_publicada_de_verdade_no_broker_real_e_published_at_e_marcado()
    {
        var token = await GetDevTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new RegisterTransactionRequest(TransactionType.Credit, 55m, DateTimeOffset.UtcNow, null)),
            Headers = { { CorrelationHeaders.IdempotencyKey, $"itest-real-broker-{Guid.NewGuid()}" } }
        };

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            var stillPending = await dbContext.OutboxMessages.CountAsync(m => m.PublishedAt == null);
            if (stillPending == 0)
            {
                return; // sucesso: o publicador drenou a Outbox contra o broker real
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.Fail("O OutboxPublisherService não publicou a mensagem pendente no broker real dentro do prazo esperado.");
    }

    [Fact]
    public async Task Mensagem_permanentemente_quebrada_nao_bloqueia_publicacao_das_demais_mensagens_do_mesmo_lote()
    {
        var brokenMessageId = Guid.NewGuid();
        var healthyMessageId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

            // OccurredAt anterior à mensagem saudável: como o publicador ordena por OccurredAt,
            // a quebrada é processada primeiro dentro do mesmo lote — é justamente esse caso
            // (poison message "na frente" da fila) que prova o isolamento.
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = brokenMessageId,
                Type = "Verity.Ledger.Tests.EventoTipoInexistente",
                Payload = "{}",
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                OccurredAt = occurredAt,
                RetryCount = 0,
            });

            var integrationEvent = new TransactionRegisteredEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                Type: ContractTransactionType.Credit,
                Amount: 20m,
                BusinessDate: DateOnly.FromDateTime(DateTime.UtcNow),
                OccurredAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid(),
                CausationId: Guid.NewGuid());

            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = healthyMessageId,
                Type = typeof(TransactionRegisteredEvent).FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent),
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                OccurredAt = occurredAt.AddMilliseconds(1),
                RetryCount = 0,
            });

            await dbContext.SaveChangesAsync();
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        OutboxMessage? healthy = null;
        OutboxMessage? broken = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            healthy = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == healthyMessageId);
            broken = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == brokenMessageId);

            if (healthy.PublishedAt is not null && broken.DeadLetteredAt is not null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        broken.Should().NotBeNull();
        broken!.DeadLetteredAt.Should().NotBeNull("um tipo de evento desconhecido nunca vai se resolver sozinho");
        broken.PublishedAt.Should().BeNull();

        healthy.Should().NotBeNull();
        healthy!.PublishedAt.Should().NotBeNull(
            "a mensagem quebrada no mesmo lote não deve impedir a publicação das demais mensagens");
        healthy.DeadLetteredAt.Should().BeNull();
    }
}
