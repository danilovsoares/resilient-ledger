using Microsoft.EntityFrameworkCore;
using Verity.DailyBalance.Application.Abstractions;

namespace Verity.DailyBalance.Infrastructure.Persistence;

public sealed class ProcessedMessageStore(DailyBalanceDbContext dbContext) : IProcessedMessageStore
{
    public Task<bool> IsProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        dbContext.ProcessedMessages.AnyAsync(p => p.EventId == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType, Guid correlationId)
    {
        dbContext.ProcessedMessages.Add(new ProcessedMessage
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        });
    }
}
