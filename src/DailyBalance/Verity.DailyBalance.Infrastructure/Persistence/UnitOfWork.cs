using Microsoft.EntityFrameworkCore;
using Npgsql;
using Verity.DailyBalance.Application.Abstractions;

namespace Verity.DailyBalance.Infrastructure.Persistence;

public sealed class UnitOfWork(DailyBalanceDbContext dbContext) : IUnitOfWork
{
    private const string PostgresUniqueViolationSqlState = "23505";

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsProcessedMessageDuplicate(ex, out var eventId))
        {
            throw new DuplicateEventException(eventId);
        }
    }

    private static bool IsProcessedMessageDuplicate(DbUpdateException ex, out Guid eventId)
    {
        eventId = Guid.Empty;

        if (ex.InnerException is not PostgresException { SqlState: PostgresUniqueViolationSqlState })
        {
            return false;
        }

        var processedMessage = ex.Entries
            .Select(e => e.Entity)
            .OfType<ProcessedMessage>()
            .FirstOrDefault();

        if (processedMessage is null)
        {
            return false;
        }

        eventId = processedMessage.EventId;
        return true;
    }
}
