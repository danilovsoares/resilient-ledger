using Verity.Ledger.Domain.Abstractions;
using Verity.Ledger.Domain.Exceptions;

namespace Verity.Ledger.Domain.Transactions;

/// <summary>
/// Lançamento financeiro diário (crédito ou débito). Agregado raiz do Ledger.
/// Imutável após criação: o escopo inicial não suporta edição ou cancelamento
/// (ver docs/future-evolution.md).
/// </summary>
public sealed class Transaction : AggregateRoot
{
    public Guid Id { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Instante em que o lançamento ocorreu, em UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Data de negócio (UTC) derivada de <see cref="OccurredAt"/>, usada para consolidação diária.</summary>
    public DateOnly BusinessDate { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Chave de idempotência fornecida pelo cliente (header Idempotency-Key).</summary>
    public string IdempotencyKey { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Preenchido quando este lançamento é, ele mesmo, o estorno de outro (ver
    /// <see cref="RegisterReversal"/>). O lançamento original nunca é alterado nem removido —
    /// "estar estornado" é uma propriedade derivada de existir um lançamento com este campo
    /// apontando para ele, não um estado mutável guardado nele.
    /// </summary>
    public Guid? ReversalOfTransactionId { get; private set; }

    private Transaction() { }

    public static Transaction Register(
        TransactionType type,
        decimal amount,
        DateTimeOffset occurredAt,
        string idempotencyKey,
        string? description)
    {
        if (amount <= 0)
        {
            throw new DomainException("O valor do lançamento deve ser positivo.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("A chave de idempotência (Idempotency-Key) é obrigatória.");
        }

        var occurredAtUtc = occurredAt.ToUniversalTime();

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = type,
            Amount = amount,
            OccurredAt = occurredAtUtc,
            BusinessDate = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
            Description = description,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow
        };

        transaction.Raise(new TransactionRegisteredDomainEvent(
            eventId: Guid.NewGuid(),
            transactionId: transaction.Id,
            type: transaction.Type,
            amount: transaction.Amount,
            businessDate: transaction.BusinessDate,
            occurredAtUtc: transaction.OccurredAt));

        return transaction;
    }

    /// <summary>
    /// Estorna <paramref name="original"/> registrando um novo lançamento, no tipo oposto e com
    /// o mesmo valor, datado de agora — não de <see cref="OccurredAt"/> do original. O lançamento
    /// original nunca é alterado: corrigir o passado em um livro-caixa é adicionar um novo
    /// registro, não reescrever um existente. Por ser aditivo como qualquer outro lançamento,
    /// não exige nenhuma mudança no consumidor do Daily Balance (ver ADR-004).
    /// </summary>
    public static Transaction RegisterReversal(Transaction original, string idempotencyKey)
    {
        var reversalType = original.Type == TransactionType.Credit ? TransactionType.Debit : TransactionType.Credit;
        var occurredAtUtc = DateTimeOffset.UtcNow;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = reversalType,
            Amount = original.Amount,
            OccurredAt = occurredAtUtc,
            BusinessDate = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
            Description = string.IsNullOrWhiteSpace(original.Description)
                ? $"Estorno do lançamento {original.Id}"
                : $"Estorno: {original.Description}",
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            ReversalOfTransactionId = original.Id
        };

        transaction.Raise(new TransactionRegisteredDomainEvent(
            eventId: Guid.NewGuid(),
            transactionId: transaction.Id,
            type: transaction.Type,
            amount: transaction.Amount,
            businessDate: transaction.BusinessDate,
            occurredAtUtc: transaction.OccurredAt));

        return transaction;
    }
}
