using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Infrastructure.Telemetry;

namespace Verity.DailyBalance.Infrastructure.Caching;

/// <summary>
/// Cache-aside com Redis (ADR-006). Se o Redis estiver indisponível, as operações não lançam
/// exceção para o chamador: um miss é reportado e a consulta cai para o PostgreSQL
/// (docs/resiliency-and-messaging.md, cenário "Redis indisponível").
/// </summary>
public sealed class RedisDailyBalanceCache(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> options,
    DailyBalanceCacheMetrics metrics,
    ILogger<RedisDailyBalanceCache> logger) : IDailyBalanceCache
{
    private readonly RedisOptions _options = options.Value;

    public async Task<DailyBalanceDto?> GetAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        try
        {
            var db = connectionMultiplexer.GetDatabase();
            var value = await db.StringGetAsync(BuildKey(businessDate));

            if (value.IsNullOrEmpty)
            {
                metrics.RecordMiss();
                return null;
            }

            metrics.RecordHit();
            return JsonSerializer.Deserialize<DailyBalanceDto>((string)value!);
        }
        catch (Exception ex)
        {
            metrics.RecordMiss();
            logger.LogWarning(ex, "Falha ao ler o cache Redis para {BusinessDate}; retornando cache miss (fallback ao PostgreSQL)", businessDate);
            return null;
        }
    }

    public async Task SetAsync(DateOnly businessDate, DailyBalanceDto value, CancellationToken cancellationToken)
    {
        try
        {
            var db = connectionMultiplexer.GetDatabase();
            await db.StringSetAsync(BuildKey(businessDate), JsonSerializer.Serialize(value), _options.TimeToLive);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao escrever no cache Redis para {BusinessDate}; a consulta seguinte fará cache miss", businessDate);
        }
    }

    public async Task InvalidateAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        try
        {
            var db = connectionMultiplexer.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(businessDate));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao invalidar o cache Redis para {BusinessDate}", businessDate);
        }
    }

    private static string BuildKey(DateOnly businessDate) => $"daily-balance:{businessDate:yyyy-MM-dd}";
}
