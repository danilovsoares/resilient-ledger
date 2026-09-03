using Microsoft.EntityFrameworkCore;
using Npgsql;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Infrastructure.Persistence;

public sealed class UnitOfWork(LedgerDbContext dbContext) : IUnitOfWork
{
    private const string PostgresUniqueViolationSqlState = "23505";

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueIdempotencyKeyViolation(ex, out var idempotencyKey))
        {
            throw new IdempotencyConflictException(idempotencyKey);
        }
    }

    private static bool IsUniqueIdempotencyKeyViolation(DbUpdateException ex, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;

        if (ex.InnerException is not PostgresException { SqlState: PostgresUniqueViolationSqlState } postgresException
            || postgresException.ConstraintName != "ix_transactions_idempotency_key")
        {
            return false;
        }

        idempotencyKey = ex.Entries
            .Select(e => e.Entity)
            .OfType<Transaction>()
            .Select(t => t.IdempotencyKey)
            .FirstOrDefault() ?? string.Empty;

        return true;
    }
}
