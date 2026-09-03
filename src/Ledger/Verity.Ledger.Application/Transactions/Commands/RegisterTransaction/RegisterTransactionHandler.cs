using Microsoft.Extensions.Logging;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Dtos;
using Verity.Ledger.Domain.Transactions;
using ContractTransactionType = Verity.Shared.Contracts.IntegrationEvents.TransactionType;
using TransactionRegisteredEvent = Verity.Shared.Contracts.IntegrationEvents.TransactionRegisteredEvent;

namespace Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;

/// <summary>
/// Registra um lançamento e, na mesma transação de banco, enfileira o evento de integração
/// correspondente na Outbox (ver ADR-003). Trata idempotência via chave fornecida pelo cliente:
/// uma repetição da mesma chave não cria um novo lançamento nem publica um novo evento.
/// </summary>
public sealed class RegisterTransactionHandler(
    ITransactionRepository repository,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ILogger<RegisterTransactionHandler> logger)
    : ICommandHandler<RegisterTransactionCommand, RegisterTransactionResult>
{
    public async Task<RegisterTransactionResult> HandleAsync(RegisterTransactionCommand command, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Requisição idempotente reaproveitada para IdempotencyKey {IdempotencyKey}, TransactionId {TransactionId}",
                command.IdempotencyKey, existing.Id);
            return new RegisterTransactionResult(TransactionDto.FromDomain(existing), IsNewRegistration: false);
        }

        var transaction = Transaction.Register(
            command.Type,
            command.Amount,
            command.OccurredAt,
            command.IdempotencyKey,
            command.Description);

        repository.Add(transaction);

        var domainEvent = transaction.DomainEvents.OfType<TransactionRegisteredDomainEvent>().Single();

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

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            // Corrida entre requisições concorrentes com a mesma Idempotency-Key: a outra
            // venceu o índice único primeiro. Buscamos o resultado definitivo já persistido.
            logger.LogInformation(
                "Conflito de idempotência para IdempotencyKey {IdempotencyKey}; reaproveitando lançamento existente",
                command.IdempotencyKey);

            var winner = await repository.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Conflito de idempotência sem lançamento correspondente.");

            return new RegisterTransactionResult(TransactionDto.FromDomain(winner), IsNewRegistration: false);
        }

        transaction.ClearDomainEvents();

        return new RegisterTransactionResult(TransactionDto.FromDomain(transaction), IsNewRegistration: true);
    }
}
