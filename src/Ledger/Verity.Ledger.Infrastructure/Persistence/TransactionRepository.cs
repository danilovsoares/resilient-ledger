using Microsoft.EntityFrameworkCore;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Infrastructure.Persistence;

public sealed class TransactionRepository(LedgerDbContext dbContext) : ITransactionRepository
{
    public void Add(Transaction transaction) => dbContext.Transactions.Add(transaction);

    public Task<Transaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetByBusinessDatePagedAsync(
        DateOnly businessDate, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.BusinessDate == businessDate);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(t => t.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> HasReversalAsync(Guid transactionId, CancellationToken cancellationToken) =>
        dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(t => t.ReversalOfTransactionId == transactionId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetReversalMapAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken) =>
        await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.ReversalOfTransactionId != null && transactionIds.Contains(t.ReversalOfTransactionId!.Value))
            .ToDictionaryAsync(t => t.ReversalOfTransactionId!.Value, t => t.Id, cancellationToken);
}
