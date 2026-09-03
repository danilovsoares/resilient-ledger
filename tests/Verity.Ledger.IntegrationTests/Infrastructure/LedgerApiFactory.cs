using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Verity.Ledger.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a Api do Ledger contra um PostgreSQL real (Testcontainers), aplicando as migrações
/// automaticamente (Database:AutoMigrate=true, como em desenvolvimento). Por padrão aponta o
/// RabbitMQ para um host inalcançável — isso prova, nos testes, que o caminho de escrita não
/// depende do broker estar disponível (ADR-002). Testes que precisam de um broker real devem
/// usar <see cref="WithReachableRabbitMq"/>.
/// </summary>
public sealed class LedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verity_ledger_test")
        .WithUsername("verity")
        .WithPassword("verity")
        .Build();

    private bool _useUnreachableBroker = true;
    private Uri? _reachableRabbitMqUri;

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Aponta a Api para um RabbitMQ real e alcançável (ex.: um Testcontainers.RabbitMq).</summary>
    public LedgerApiFactory WithReachableRabbitMq(Uri rabbitMqUri)
    {
        _useUnreachableBroker = false;
        _reachableRabbitMqUri = rabbitMqUri;
        return this;
    }

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:LedgerDb"] = _postgres.GetConnectionString(),
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-for-production-use-32bytes+",
                ["Database:AutoMigrate"] = "true",
                ["RateLimiting:PermitLimit"] = "1000",
                ["RateLimiting:LoginPermitLimit"] = "1000",
                ["OutboxPublisher:PollingInterval"] = "00:00:01",
            };

            if (_useUnreachableBroker)
            {
                // Porta padrão do RabbitMQ (5672) fechada em localhost neste ambiente de teste
                // (o RabbitMQ real do docker-compose, quando ativo, é mapeado em 5673) —
                // falha de conexão rápida, sem depender de DNS.
                overrides["RabbitMq:Host"] = "127.0.0.1";
            }
            else if (_reachableRabbitMqUri is not null)
            {
                var userInfo = _reachableRabbitMqUri.UserInfo.Split(':');
                overrides["RabbitMq:Host"] = _reachableRabbitMqUri.Host;
                overrides["RabbitMq:Port"] = _reachableRabbitMqUri.Port.ToString();
                overrides["RabbitMq:Username"] = userInfo[0];
                overrides["RabbitMq:Password"] = userInfo[1];
            }

            config.AddInMemoryCollection(overrides);
        });
    }
}
