namespace Verity.Ledger.Application.Transactions.Commands.CancelTransaction;

public sealed record CancelTransactionCommand(Guid TransactionId, Guid CorrelationId);
