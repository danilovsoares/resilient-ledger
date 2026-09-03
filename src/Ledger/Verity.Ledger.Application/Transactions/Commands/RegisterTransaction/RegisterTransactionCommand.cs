using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;

public sealed record RegisterTransactionCommand(
    TransactionType Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description,
    string IdempotencyKey,
    Guid CorrelationId);
