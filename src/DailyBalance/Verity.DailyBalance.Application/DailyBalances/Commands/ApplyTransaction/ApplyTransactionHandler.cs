using Microsoft.Extensions.Logging;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Application.Common;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;

/// <summary>
/// Aplica o efeito de um lançamento à projeção de saldo diário de forma idempotente.
/// Deduplicação via Inbox (processed_messages) acontece antes de qualquer alteração de estado;
/// projeção e marca de processamento são persistidas na mesma transação (ADR-003, ADR-004).
/// </summary>
public sealed class ApplyTransactionHandler(
    IDailyBalanceRepository repository,
    IProcessedMessageStore processedMessages,
    IDailyBalanceCache cache,
    IUnitOfWork unitOfWork,
    ILogger<ApplyTransactionHandler> logger)
    : ICommandHandler<ApplyTransactionCommand, ApplyTransactionResult>
{
    public async Task<ApplyTransactionResult> HandleAsync(ApplyTransactionCommand command, CancellationToken cancellationToken)
    {
        if (await processedMessages.IsProcessedAsync(command.EventId, cancellationToken))
        {
            logger.LogInformation(
                "Evento {EventId} já processado anteriormente; reentrega ignorada (no-op idempotente)",
                command.EventId);
            return new ApplyTransactionResult(WasAlreadyProcessed: true);
        }

        var dailyBalance = await repository.GetByBusinessDateAsync(command.BusinessDate, cancellationToken);
        var isNew = dailyBalance is null;
        dailyBalance ??= DailyBalanceAggregate.CreateEmpty(command.BusinessDate);

        dailyBalance.Apply(command.Kind, command.Amount);

        repository.Upsert(dailyBalance, isNew);
        processedMessages.MarkProcessed(command.EventId, nameof(ApplyTransactionCommand), command.CorrelationId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateEventException)
        {
            // Corrida entre consumidores concorrentes para o mesmo EventId: nossa transação
            // (incluindo o incremento de saldo) foi revertida por completo. O outro consumidor
            // já aplicou o efeito — tratamos como no-op idempotente.
            logger.LogInformation(
                "Corrida de deduplicação detectada para o evento {EventId}; outra instância já aplicou o efeito",
                command.EventId);
            return new ApplyTransactionResult(WasAlreadyProcessed: true);
        }

        // Cache-aside: invalida a entrada para que a próxima leitura repopule com o valor
        // consistente recém-persistido (ADR-006).
        await cache.InvalidateAsync(command.BusinessDate, cancellationToken);

        logger.LogInformation(
            "Saldo diário de {BusinessDate} atualizado a partir do evento {EventId} (CorrelationId {CorrelationId})",
            command.BusinessDate, command.EventId, command.CorrelationId);

        return new ApplyTransactionResult(WasAlreadyProcessed: false);
    }
}
