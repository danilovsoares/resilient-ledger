namespace Verity.Shared.Contracts.IntegrationEvents;

/// <summary>
/// Evento de integração publicado pelo Ledger, via Transactional Outbox, sempre que um
/// lançamento é registrado com sucesso. Consumido pelo Daily Balance Worker para atualizar
/// a projeção de saldo diário.
/// </summary>
/// <param name="EventId">
/// Identidade única do evento. Usada pelo consumidor como chave de deduplicação
/// (Inbox/ProcessedMessages) para garantir que reentregas do broker não dupliquem o efeito no saldo.
/// </param>
/// <param name="TransactionId">Identidade do lançamento de origem no Ledger.</param>
/// <param name="Type">Tipo do lançamento (crédito ou débito).</param>
/// <param name="Amount">Valor do lançamento. Sempre positivo; o sinal é dado por <see cref="Type"/>.</param>
/// <param name="BusinessDate">Data de negócio (UTC) à qual o lançamento pertence.</param>
/// <param name="OccurredAtUtc">Instante em que o lançamento ocorreu, em UTC.</param>
/// <param name="CorrelationId">Identificador da jornada de negócio, propagado desde o request HTTP original.</param>
/// <param name="CausationId">Identificador da mensagem/ação imediatamente anterior que causou este evento.</param>
public sealed record TransactionRegisteredEvent(
    Guid EventId,
    Guid TransactionId,
    TransactionType Type,
    decimal Amount,
    DateOnly BusinessDate,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId,
    Guid CausationId);
