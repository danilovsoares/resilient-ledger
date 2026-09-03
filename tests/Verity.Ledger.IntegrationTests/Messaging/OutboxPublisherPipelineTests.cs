using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;
using Verity.Ledger.Api.Controllers;
using Verity.Ledger.Domain.Transactions;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.IntegrationTests.Infrastructure;
using Verity.Shared.Contracts.Correlation;

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
}
