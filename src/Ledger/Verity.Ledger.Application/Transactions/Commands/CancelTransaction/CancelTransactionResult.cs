using Verity.Ledger.Application.Transactions.Dtos;

namespace Verity.Ledger.Application.Transactions.Commands.CancelTransaction;

public sealed record CancelTransactionResult(TransactionDto Reversal);
