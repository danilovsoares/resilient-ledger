using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Infrastructure.Persistence;

namespace Verity.Ledger.Infrastructure.Telemetry;

/// <summary>
/// Métricas customizadas do Ledger, publicadas via System.Diagnostics.Metrics e coletadas
/// pelo OpenTelemetry (ver docs/observability.md). Instanciada como singleton; o gauge é
/// recalculado sob demanda a cada coleta pelo exportador configurado.
/// </summary>
public sealed class LedgerMetrics : IDisposable
{
    public const string MeterName = "Verity.Ledger";

    private readonly Meter _meter;
    private readonly IServiceScopeFactory _scopeFactory;

    public LedgerMetrics(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _meter = new Meter(MeterName);

        _meter.CreateObservableGauge(
            "verity.ledger.outbox.pending",
            ObservePendingOutboxCount,
            unit: "{messages}",
            description: "Quantidade de mensagens na Outbox aguardando publicação no broker.");
    }

    private IEnumerable<Measurement<long>> ObservePendingOutboxCount()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var count = dbContext.OutboxMessages.LongCount(m => m.PublishedAt == null);
        yield return new Measurement<long>(count);
    }

    public void Dispose() => _meter.Dispose();
}
