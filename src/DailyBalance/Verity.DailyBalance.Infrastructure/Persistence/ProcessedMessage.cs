namespace Verity.DailyBalance.Infrastructure.Persistence;

/// <summary>
/// Registro da Inbox: um evento por linha, chaveado por <c>EventId</c>. A existência de uma
/// linha para um EventId indica que o efeito daquele evento já foi aplicado à projeção
/// (ver ADR-003, ADR-004).
/// </summary>
public sealed class ProcessedMessage
{
    public Guid EventId { get; init; }
    public required string EventType { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
    public Guid CorrelationId { get; init; }
}
