using System.Text.Json;
using Verity.Ledger.Application.Abstractions;

namespace Verity.Ledger.Infrastructure.Persistence;

public sealed class OutboxWriter(LedgerDbContext dbContext) : IOutboxWriter
{
    public void Enqueue<TEvent>(TEvent integrationEvent, Guid correlationId, Guid causationId) where TEvent : notnull
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(TEvent).FullName ?? typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(integrationEvent),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        dbContext.OutboxMessages.Add(message);
    }
}
