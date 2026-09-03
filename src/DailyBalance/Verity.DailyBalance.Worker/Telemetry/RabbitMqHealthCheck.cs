using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Verity.DailyBalance.Infrastructure.Messaging;

namespace Verity.DailyBalance.Worker.Telemetry;

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
