using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Domain.DailyBalances;
using Verity.DailyBalance.IntegrationTests.Infrastructure;

namespace Verity.DailyBalance.IntegrationTests.DailyBalances;

[Collection(DailyBalanceIntegrationCollection.Name)]
public sealed class GetDailyBalanceEndpointTests : IAsyncLifetime
{
    private readonly DailyBalanceApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Data_sem_lancamentos_retorna_200_com_saldo_zerado_em_vez_de_404()
    {
        var response = await _client.GetAsync("/api/v1/daily-balances/2099-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DailyBalanceDto>();
        body!.Balance.Should().Be(0m);
        body.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Apos_aplicar_transacao_a_consulta_reflete_o_saldo_e_popula_o_cache_redis()
    {
        var businessDate = new DateOnly(2026, 3, 10);

        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ApplyTransactionCommand, ApplyTransactionResult>>();

            await handler.HandleAsync(new ApplyTransactionCommand(
                Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Credit, 200m, businessDate, Guid.NewGuid()),
                CancellationToken.None);
        }

        var response = await _client.GetAsync($"/api/v1/daily-balances/{businessDate:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DailyBalanceDto>();
        body!.Balance.Should().Be(200m);

        using var redisScope = _factory.Services.CreateScope();
        var connectionMultiplexer = redisScope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var cached = await connectionMultiplexer.GetDatabase().StringGetAsync($"daily-balance:{businessDate:yyyy-MM-dd}");
        cached.IsNullOrEmpty.Should().BeFalse("a consulta em cache miss deve repopular o Redis (cache-aside, ADR-006)");
    }
}
