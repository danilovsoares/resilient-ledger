namespace Verity.Ledger.Application.Abstractions;

/// <summary>
/// Grava mensagens pendentes na tabela <c>outbox_messages</c> como parte da mesma unidade de
/// trabalho (transação) do agregado que originou o evento. A publicação efetiva no broker é
/// feita, de forma assíncrona, pelo Outbox Publisher (ver Infrastructure).
/// </summary>
public interface IOutboxWriter
{
    void Enqueue<TEvent>(TEvent integrationEvent, Guid correlationId, Guid causationId) where TEvent : notnull;
}
