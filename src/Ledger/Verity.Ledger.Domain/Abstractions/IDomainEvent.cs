namespace Verity.Ledger.Domain.Abstractions;

/// <summary>
/// Marcador para eventos de domínio levantados por agregados. Eventos de domínio são internos
/// ao processo do Ledger; a tradução para um evento de integração publicado no broker acontece
/// na camada de Application/Infrastructure (ver Outbox).
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}
