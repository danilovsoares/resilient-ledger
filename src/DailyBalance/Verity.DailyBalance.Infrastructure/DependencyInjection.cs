using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Infrastructure.Caching;
using Verity.DailyBalance.Infrastructure.Messaging;
using Verity.DailyBalance.Infrastructure.Persistence;
using Verity.DailyBalance.Infrastructure.Telemetry;

namespace Verity.DailyBalance.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registrado tanto pela Api (leitura) quanto pelo Worker (escrita da projeção).</summary>
    public static IServiceCollection AddDailyBalancePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DailyBalanceDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString("DailyBalanceDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "daily_balance"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IDailyBalanceRepository, DailyBalanceRepository>();
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            // Resolvido via IOptions (tardio) em vez de uma leitura direta e antecipada de
            // IConfiguration, para respeitar corretamente qualquer configuração adicionada
            // depois deste registro (por exemplo, overrides de WebApplicationFactory em testes
            // de integração).
            var redisOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>().Value;
            var redisConfig = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            // Não aborta a inicialização se o Redis estiver momentaneamente indisponível: a
            // aplicação deve subir normalmente e cair no fallback ao PostgreSQL até o Redis
            // voltar (ver ADR-006 e docs/resiliency-and-messaging.md).
            redisConfig.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisConfig);
        });
        services.AddScoped<IDailyBalanceCache, RedisDailyBalanceCache>();
        services.AddSingleton<DailyBalanceCacheMetrics>();

        return services;
    }

    /// <summary>Registrado apenas pelo Worker: consumidor MassTransit do evento de lançamento.</summary>
    public static IServiceCollection AddDailyBalanceMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            bus.AddConsumer<TransactionRegisteredConsumer>(cfg =>
            {
                // Retry com backoff exponencial e jitter antes de encaminhar à fila de erro
                // (DLQ criada automaticamente pelo MassTransit para o receive endpoint).
                cfg.UseMessageRetry(retry => retry.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromMilliseconds(200),
                    maxInterval: TimeSpan.FromSeconds(10),
                    intervalDelta: TimeSpan.FromMilliseconds(200)));
            });

            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMq = context.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;

                cfg.Host(rabbitMq.Host, rabbitMq.Port, rabbitMq.VirtualHost, host =>
                {
                    host.Username(rabbitMq.Username);
                    host.Password(rabbitMq.Password);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
