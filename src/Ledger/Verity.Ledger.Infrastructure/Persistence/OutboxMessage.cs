namespace Verity.Ledger.Infrastructure.Persistence;

/// <summary>
/// Registro da Transactional Outbox (ADR-003). Gravado na mesma transação de banco que o
/// agregado que originou o evento; publicado de forma assíncrona pelo <c>OutboxPublisherService</c>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid CausationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Marcada quando a falha é permanente (tipo de evento desconhecido ou payload corrompido —
    /// ver <see cref="OutboxPublisherService"/>): retentar não vai resolver, então a mensagem
    /// para de ser selecionada para publicação automática e fica visível para investigação
    /// manual. Falhas transitórias (ex.: broker indisponível) nunca marcam este campo — essas
    /// continuam sendo retentadas indefinidamente, sem limite (ADR-002/ADR-003).
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }
}
