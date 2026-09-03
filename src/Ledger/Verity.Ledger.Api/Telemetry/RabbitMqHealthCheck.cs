using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Verity.Ledger.Infrastructure.Messaging;

namespace Verity.Ledger.Api.Telemetry;

/// <summary>
/// Verifica conectividade com o RabbitMQ abrindo (e fechando) uma conexão de curta duração.
/// Usado no health check "ready": se o broker estiver fora do ar, /health/ready reporta
/// Unhealthy, mas isso não impede o Ledger de aceitar e persistir novos lançamentos — apenas a
/// publicação da Outbox fica temporariamente pendente (ADR-002).
/// </summary>
public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var rabbitMq = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = rabbitMq.Host,
                VirtualHost = rabbitMq.VirtualHost,
                UserName = rabbitMq.Username,
                Password = rabbitMq.Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Falha ao conectar ao RabbitMQ", ex);
        }
    }
}
