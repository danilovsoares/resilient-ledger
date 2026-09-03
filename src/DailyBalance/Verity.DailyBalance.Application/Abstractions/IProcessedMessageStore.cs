namespace Verity.DailyBalance.Application.Abstractions;

/// <summary>
/// Implementa o padrão Inbox: registra o <c>EventId</c> de cada evento efetivamente aplicado
/// à projeção, na mesma transação de banco, para impedir que reentregas do broker dupliquem
/// o efeito no saldo (ver ADR-003 e ADR-004).
/// </summary>
public interface IProcessedMessageStore
{
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken cancellationToken);

    void MarkProcessed(Guid eventId, string eventType, Guid correlationId);
}
