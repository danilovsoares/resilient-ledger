using System.Diagnostics.Metrics;

namespace Verity.DailyBalance.Infrastructure.Telemetry;

/// <summary>
/// Contadores de acerto/erro do cache-aside de saldo diário (ADR-006). A razão de cache hit
/// (hits / (hits + misses)) é derivada destes dois contadores pelo backend de observabilidade
/// (ver docs/observability.md). Um "miss" inclui tanto ausência da chave quanto falha de
/// conexão com o Redis — em ambos os casos a consulta cai para o PostgreSQL.
/// </summary>
public sealed class DailyBalanceCacheMetrics
{
    public const string MeterName = "Verity.DailyBalance";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;

    public DailyBalanceCacheMetrics()
    {
        var meter = new Meter(MeterName);
        _hits = meter.CreateCounter<long>("verity.dailybalance.cache.hits", unit: "{queries}");
        _misses = meter.CreateCounter<long>("verity.dailybalance.cache.misses", unit: "{queries}");
    }

    public void RecordHit() => _hits.Add(1);

    public void RecordMiss() => _misses.Add(1);
}
