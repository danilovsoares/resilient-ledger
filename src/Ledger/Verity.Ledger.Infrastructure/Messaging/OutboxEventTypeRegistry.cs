using Verity.Shared.Contracts.IntegrationEvents;

namespace Verity.Ledger.Infrastructure.Messaging;

/// <summary>
/// Mapa de nome de tipo (gravado na coluna <c>outbox_messages.type</c>) para o tipo CLR
/// correspondente, usado para desserializar o payload antes de publicar no barramento.
/// </summary>
public static class OutboxEventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> Types = new Dictionary<string, Type>
    {
        [typeof(TransactionRegisteredEvent).FullName!] = typeof(TransactionRegisteredEvent)
    };

    public static Type Resolve(string typeName) =>
        Types.TryGetValue(typeName, out var type)
            ? type
            : throw new InvalidOperationException($"Tipo de evento de outbox desconhecido: {typeName}");
}
