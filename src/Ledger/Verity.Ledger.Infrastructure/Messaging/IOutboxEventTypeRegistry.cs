namespace Verity.Ledger.Infrastructure.Messaging;

/// <summary>
/// Resolve o tipo CLR correspondente ao nome gravado em <c>outbox_messages.type</c>, usado
/// para desserializar o payload antes de publicar no barramento (ver
/// <see cref="OutboxPublisherService"/>).
/// </summary>
public interface IOutboxEventTypeRegistry
{
    Type Resolve(string typeName);
}
