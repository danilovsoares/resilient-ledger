using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Verity.DailyBalance.Api.Telemetry;

/// <summary>
/// Reporta o estado da conexão com o Redis. Diferente do PostgreSQL, uma falha aqui não torna
/// a consulta de saldo indisponível — apenas mais lenta, pois cai para o fallback no
/// PostgreSQL (ver ADR-006). Ainda assim é reportado em /health/ready para visibilidade
/// operacional.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await connectionMultiplexer.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Latência: {latency.TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Falha ao conectar ao Redis; consultas usarão fallback ao PostgreSQL", ex);
        }
    }
}
