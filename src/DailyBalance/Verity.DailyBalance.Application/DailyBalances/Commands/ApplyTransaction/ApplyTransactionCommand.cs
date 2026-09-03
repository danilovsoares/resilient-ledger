using Verity.DailyBalance.Domain.DailyBalances;

namespace Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;

/// <summary>Comando derivado do evento de integração <c>TransactionRegisteredEvent</c>.</summary>
public sealed record ApplyTransactionCommand(
    Guid EventId,
    Guid TransactionId,
    TransactionKind Kind,
    decimal Amount,
    DateOnly BusinessDate,
    Guid CorrelationId);
