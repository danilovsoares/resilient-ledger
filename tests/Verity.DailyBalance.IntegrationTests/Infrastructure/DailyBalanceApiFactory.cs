using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Verity.DailyBalance.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a Api do Daily Balance contra PostgreSQL e Redis reais (Testcontainers), com as
/// migrações aplicadas automaticamente. O RabbitMQ não é necessário aqui: os testes de
/// consumo/Inbox chamam o handler de aplicação diretamente (via DI), sem depender de um
/// consumidor MassTransit real — a garantia de deduplicação está na camada de persistência,
/// não no transporte (ver ADR-003/ADR-004).
/// </summary>
public sealed class DailyBalanceApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verity_daily_balance_test")
        .WithUsername("verity")
        .WithPassword("verity")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    public new async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DailyBalanceDb"] = _postgres.GetConnectionString(),
                ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                ["Database:AutoMigrate"] = "true",
                ["RateLimiting:PermitLimit"] = "1000",
            });
        });
    }
}
