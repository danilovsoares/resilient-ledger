using MassTransit;
using Microsoft.Extensions.Logging;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;
using Verity.DailyBalance.Domain.DailyBalances;
using Verity.Shared.Contracts.IntegrationEvents;

namespace Verity.DailyBalance.Infrastructure.Messaging;

/// <summary>
/// Consome <see cref="TransactionRegisteredEvent"/> e atualiza a projeção de saldo diário.
/// O MassTransit só confirma (ACK) a mensagem ao broker após este método concluir sem exceção —
/// ou seja, depois que a transação de banco (projeção + Inbox) foi commitada (ver
/// docs/resiliency-and-messaging.md). Falhas são retentadas com backoff exponencial e jitter
/// (configurado no DependencyInjection) antes de irem para a fila de erro (DLQ).
/// </summary>
public sealed class TransactionRegisteredConsumer(
    ICommandHandler<ApplyTransactionCommand, ApplyTransactionResult> handler,
    ILogger<TransactionRegisteredConsumer> logger) : IConsumer<TransactionRegisteredEvent>
{
    public async Task Consume(ConsumeContext<TransactionRegisteredEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Evento {EventId} (MessageId {MessageId}) recebido para TransactionId {TransactionId} (CorrelationId {CorrelationId})",
            message.EventId, context.MessageId, message.TransactionId, message.CorrelationId);

        var command = new ApplyTransactionCommand(
            message.EventId,
            message.TransactionId,
            message.Type == TransactionType.Credit ? TransactionKind.Credit : TransactionKind.Debit,
            message.Amount,
            message.BusinessDate,
            message.CorrelationId);

        await handler.HandleAsync(command, context.CancellationToken);
    }
}
