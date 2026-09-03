using Verity.Ledger.Domain.Abstractions;

namespace Verity.Ledger.Domain.Transactions;

public sealed class TransactionRegisteredDomainEvent(
    Guid eventId,
    Guid transactionId,
    TransactionType type,
    decimal amount,
    DateOnly businessDate,
    DateTimeOffset occurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; } = eventId;
    public Guid TransactionId { get; } = transactionId;
    public TransactionType Type { get; } = type;
    public decimal Amount { get; } = amount;
    public DateOnly BusinessDate { get; } = businessDate;
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
}
