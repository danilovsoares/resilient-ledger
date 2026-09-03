using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Application.Transactions.Dtos;

public sealed record TransactionDto(
    Guid Id,
    TransactionType Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    DateOnly BusinessDate,
    string? Description,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId)
{
    public static TransactionDto FromDomain(Transaction transaction, Guid? reversedByTransactionId = null) => new(
        transaction.Id,
        transaction.Type,
        transaction.Amount,
        transaction.OccurredAt,
        transaction.BusinessDate,
        transaction.Description,
        transaction.IdempotencyKey,
        transaction.CreatedAt,
        transaction.ReversalOfTransactionId,
        reversedByTransactionId);
}
