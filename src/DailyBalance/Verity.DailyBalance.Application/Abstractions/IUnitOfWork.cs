namespace Verity.DailyBalance.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Persiste a projeção de saldo e o registro de Inbox (processed_messages) na mesma
    /// transação de banco. Lança <see cref="DuplicateEventException"/> se dois consumidores
    /// concorrentes processarem o mesmo EventId simultaneamente (o perdedor tem toda a sua
    /// transação revertida, incluindo o incremento de saldo — sem duplicidade).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class DuplicateEventException(Guid eventId)
    : Exception($"O evento '{eventId}' já foi processado por outra transação concorrente.")
{
    public Guid EventId { get; } = eventId;
}
