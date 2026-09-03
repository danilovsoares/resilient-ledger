using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Verity.DailyBalance.Application;
using Verity.DailyBalance.Infrastructure;
using Verity.DailyBalance.Infrastructure.Persistence;
using Verity.Shared.Contracts.IntegrationEvents;

namespace Verity.DailyBalance.IntegrationTests.Messaging;

/// <summary>
/// Prova o pipeline de consumo REAL — RabbitMQ de verdade, o <c>TransactionRegisteredConsumer</c>
/// de verdade (não o handler de aplicação chamado diretamente) — ao contrário dos demais testes
/// de integração deste projeto, que testam a Inbox chamando o handler diretamente (ver
/// <see cref="Infrastructure.DailyBalanceApiFactory"/>). Reproduz a composição de DI exata do
/// Worker (<c>AddDailyBalanceMessaging</c>), incluindo a política de retry configurada.
/// </summary>
[Collection(Infrastructure.DailyBalanceIntegrationCollection.Name)]
public sealed class TransactionRegisteredConsumerPipelineTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("verity_daily_balance_pipeline_test")
        .WithUsername("verity")
        .WithPassword("verity")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    private IHost _host = null!;
    private Uri _rabbitUri = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());

        _rabbitUri = new Uri(_rabbitMq.GetConnectionString());
        var rabbitUri = _rabbitUri;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DailyBalanceDb"] = _postgres.GetConnectionString(),
                ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                ["RabbitMq:Host"] = rabbitUri.Host,
                ["RabbitMq:Port"] = rabbitUri.Port.ToString(),
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Username"] = rabbitUri.UserInfo.Split(':')[0],
                ["RabbitMq:Password"] = rabbitUri.UserInfo.Split(':')[1],
            })
            .Build();

        // Mesma composição de serviços do Worker real (Program.cs): Application + Persistence
        // (repositórios, Inbox, cache Redis) + Messaging (o consumidor MassTransit real, com a
        // mesma política de retry configurada em produção).
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDailyBalanceApplication();
                services.AddDailyBalancePersistence(configuration);
                services.AddDailyBalanceMessaging(configuration);
            })
            .Build();

        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync(), _rabbitMq.StopAsync());
    }

    [Fact]
    public async Task Evento_publicado_no_broker_real_e_consumido_e_atualiza_o_saldo()
    {
        var businessDate = new DateOnly(2026, 5, 10);
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var integrationEvent = new TransactionRegisteredEvent(
            EventId: eventId,
            TransactionId: Guid.NewGuid(),
            Type: TransactionType.Credit,
            Amount: 120m,
            BusinessDate: businessDate,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            CausationId: correlationId);

        var balance = await PublishAndWaitForBalanceAsync(integrationEvent, businessDate, expectedCredits: 120m);

        balance.TotalCredits.Should().Be(120m);

        using var scope = _host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var processedMessage = await dbContext.ProcessedMessages.SingleAsync(p => p.EventId == eventId);
        processedMessage.CorrelationId.Should().Be(correlationId,
            "o CorrelationId deve trafegar do payload do evento até a Inbox, mesmo passando por um broker real");
    }

    [Fact]
    public async Task Reentrega_do_mesmo_EventId_pelo_broker_real_nao_duplica_o_saldo()
    {
        var businessDate = new DateOnly(2026, 5, 11);
        var eventId = Guid.NewGuid();

        var integrationEvent = new TransactionRegisteredEvent(
            EventId: eventId,
            TransactionId: Guid.NewGuid(),
            Type: TransactionType.Debit,
            Amount: 30m,
            BusinessDate: businessDate,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid());

        // Simula a reentrega at-least-once do broker: a mesma identidade de evento (EventId)
        // chega duas vezes ao consumidor real. A Inbox deve garantir que o efeito é aplicado
        // uma única vez (ver ADR-003/ADR-004).
        var balanceAfterFirst = await PublishAndWaitForBalanceAsync(integrationEvent, businessDate, expectedDebits: 30m);
        balanceAfterFirst.TotalDebits.Should().Be(30m);

        var publishEndpoint = _host.Services.GetRequiredService<IPublishEndpoint>();
        await publishEndpoint.Publish(integrationEvent);

        // Não há um segundo estado para "esperar" (a reentrega é um no-op) — aguarda-se uma
        // janela plausível de processamento e confirma-se que o total não mudou.
        await Task.Delay(TimeSpan.FromSeconds(3));

        using var scope = _host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var balance = await dbContext.DailyBalances.SingleAsync(b => b.BusinessDate == businessDate);
        balance.TotalDebits.Should().Be(30m, "a reentrega pelo broker real não deve duplicar o efeito no saldo");

        var processedCount = await dbContext.ProcessedMessages.CountAsync(p => p.EventId == eventId);
        processedCount.Should().Be(1, "a Inbox deve conter apenas um registro para o EventId, mesmo com reentrega");
    }

    [Fact]
    public async Task Falha_persistente_no_consumidor_esgota_o_retry_e_a_mensagem_vai_para_a_fila_de_erro()
    {
        // Derruba o PostgreSQL real (não um mock) para forçar toda tentativa de consumo a
        // lançar uma exceção genuína — o mesmo caminho de código que rodaria em produção se o
        // banco do Daily Balance ficasse indisponível durante o processamento de um evento.
        await _postgres.StopAsync();

        var integrationEvent = new TransactionRegisteredEvent(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            Type: TransactionType.Credit,
            Amount: 10m,
            BusinessDate: new DateOnly(2026, 5, 12),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid());

        var publishEndpoint = _host.Services.GetRequiredService<IPublishEndpoint>();
        await publishEndpoint.Publish(integrationEvent);

        // UseMessageRetry(retry.Exponential(retryLimit: 5, ...)) — ver
        // AddDailyBalanceMessaging — precisa esgotar 5 tentativas reais contra um banco
        // genuinamente fora do ar antes de a mensagem ser encaminhada à fila de erro
        // (convenção MassTransit: "{fila}_error"). 90s dá margem para isso mais a latência
        // observada de conexão a um Postgres derrubado.
        const string ErrorQueueName = "transaction-registered_error";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        uint messageCount = 0;

        while (DateTimeOffset.UtcNow < deadline && messageCount == 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            messageCount = await TryGetQueueMessageCountAsync(ErrorQueueName);
        }

        messageCount.Should().BeGreaterThan(0,
            "após esgotar as tentativas de retry contra um banco indisponível, a mensagem deve chegar à fila de erro (DLQ) em vez de ser perdida ou bloquear a fila principal");
    }

    private async Task<uint> TryGetQueueMessageCountAsync(string queueName)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _rabbitUri.Host,
                Port = _rabbitUri.Port,
                UserName = _rabbitUri.UserInfo.Split(':')[0],
                Password = _rabbitUri.UserInfo.Split(':')[1],
            };

            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            var result = await channel.QueueDeclarePassiveAsync(queueName);
            return result.MessageCount;
        }
        catch
        {
            // A fila de erro só é criada pelo MassTransit quando a primeira mensagem
            // efetivamente esgota o retry — até lá, a checagem passiva falha normalmente.
            return 0;
        }
    }

    /// <summary>
    /// Publica o evento e aguarda seu efeito aparecer no saldo, republicando periodicamente a
    /// mesma identidade de evento (mesmo <c>EventId</c>) enquanto o prazo não se esgota. Isso é
    /// seguro — a Inbox deduplica por EventId (ADR-003/ADR-004) — e também exercita, contra o
    /// broker real, a garantia de que reentregas não corrompem o resultado. A republicação
    /// existe para tolerar a latência inicial (às vezes vários segundos) da primeira publicação
    /// de um bus recém-iniciado neste ambiente de teste, sem depender de um valor de timeout
    /// único e frágil.
    /// </summary>
    private async Task<Domain.DailyBalances.DailyBalance> PublishAndWaitForBalanceAsync(
        TransactionRegisteredEvent integrationEvent,
        DateOnly businessDate,
        decimal? expectedCredits = null,
        decimal? expectedDebits = null)
    {
        var publishEndpoint = _host.Services.GetRequiredService<IPublishEndpoint>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var nextPublishAt = DateTimeOffset.MinValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (DateTimeOffset.UtcNow >= nextPublishAt)
            {
                await publishEndpoint.Publish(integrationEvent);
                nextPublishAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }

            using var scope = _host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
            var balance = await dbContext.DailyBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BusinessDate == businessDate);

            if (balance is not null
                && (expectedCredits is null || balance.TotalCredits == expectedCredits)
                && (expectedDebits is null || balance.TotalDebits == expectedDebits))
            {
                return balance;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            $"O consumidor real não aplicou o evento ao saldo de {businessDate:yyyy-MM-dd} dentro do prazo esperado, mesmo após republicações.");
    }
}
