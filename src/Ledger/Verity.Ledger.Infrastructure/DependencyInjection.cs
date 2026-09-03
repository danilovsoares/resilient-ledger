using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Infrastructure.Messaging;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.Infrastructure.Security;
using Verity.Ledger.Infrastructure.Telemetry;

namespace Verity.Ledger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LedgerDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString("LedgerDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ledger"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton(configuration.GetSection(DefaultUserSeedOptions.SectionName).Get<DefaultUserSeedOptions>() ?? new DefaultUserSeedOptions());

        services.Configure<OutboxPublisherOptions>(configuration.GetSection(OutboxPublisherOptions.SectionName));
        services.AddHostedService<OutboxPublisherService>();

        services.AddSingleton<LedgerMetrics>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((context, cfg) =>
            {
                // Resolvido via IOptions no momento da construção do bus (tardio), não por uma
                // leitura antecipada de IConfiguration — necessário para respeitar overrides de
                // configuração aplicados após este registro (ex.: WebApplicationFactory em testes).
                var rabbitMq = context.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;

                cfg.Host(rabbitMq.Host, rabbitMq.Port, rabbitMq.VirtualHost, host =>
                {
                    host.Username(rabbitMq.Username);
                    host.Password(rabbitMq.Password);
                });

                // O Ledger apenas publica eventos; não possui receive endpoints/consumidores.
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
