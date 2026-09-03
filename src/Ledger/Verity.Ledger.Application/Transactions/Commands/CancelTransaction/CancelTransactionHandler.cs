using Microsoft.Extensions.Logging;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Dtos;
using Verity.Ledger.Domain.Exceptions;
using Verity.Ledger.Domain.Transactions;
using ContractTransactionType = Verity.Shared.Contracts.IntegrationEvents.TransactionType;
using TransactionRegisteredEvent = Verity.Shared.Contracts.IntegrationEvents.TransactionRegisteredEvent;

namespace Verity.Ledger.Application.Transactions.Commands.CancelTransaction;

/// <summary>
/// Estorna um lançamento registrando um novo, de tipo oposto (ver
/// <see cref="Transaction.RegisterReversal"/>), na mesma transação de banco que enfileira o
/// evento de integração correspondente (ver ADR-003) — igual a qualquer outro lançamento novo,
/// o Daily Balance não precisa saber que se trata de um estorno.
/// </summary>
public sealed class CancelTransactionHandler(
    ITransactionRepository repository,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ILogger<CancelTransactionHandler> logger)
    : ICommandHandler<CancelTransactionCommand, CancelTransactionResult?>
{
    public async Task<CancelTransactionResult?> HandleAsync(CancelTransactionCommand command, CancellationToken cancellationToken)
    {
        var original = await repository.GetByIdAsync(command.TransactionId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        if (await repository.HasReversalAsync(original.Id, cancellationToken))
        {
            throw new DomainException("Este lançamento já foi estornado.");
        }

        var reversal = Transaction.RegisterReversal(original, Guid.NewGuid().ToString());
        repository.Add(reversal);

        var domainEvent = reversal.DomainEvents.OfType<TransactionRegisteredDomainEvent>().Single();

        var integrationEvent = new TransactionRegisteredEvent(
            EventId: domainEvent.EventId,
            TransactionId: domainEvent.TransactionId,
            Type: domainEvent.Type == TransactionType.Credit ? ContractTransactionType.Credit : ContractTransactionType.Debit,
            Amount: domainEvent.Amount,
            BusinessDate: domainEvent.BusinessDate,
            OccurredAtUtc: domainEvent.OccurredAtUtc,
            CorrelationId: command.CorrelationId,
            CausationId: command.CorrelationId);

        outbox.Enqueue(integrationEvent, command.CorrelationId, command.CorrelationId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Lançamento {TransactionId} estornado pelo lançamento {ReversalId}",
            original.Id, reversal.Id);

        reversal.ClearDomainEvents();

        return new CancelTransactionResult(TransactionDto.FromDomain(reversal));
    }
}
