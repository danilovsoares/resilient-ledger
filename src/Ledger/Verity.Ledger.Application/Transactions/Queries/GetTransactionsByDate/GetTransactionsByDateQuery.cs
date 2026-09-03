namespace Verity.Ledger.Application.Transactions.Queries.GetTransactionsByDate;

public sealed record GetTransactionsByDateQuery(DateOnly BusinessDate, int Page = 1, int PageSize = 10);
