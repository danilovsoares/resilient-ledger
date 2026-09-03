using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Application.Abstractions;

public interface ITransactionRepository
{
    void Add(Transaction transaction);

    Task<Transaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Página de lançamentos de uma data de negócio, ordenados por <see cref="Transaction.OccurredAt"/>, junto com o total de lançamentos naquela data.</summary>
    Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetByBusinessDatePagedAsync(
        DateOnly businessDate, int page, int pageSize, CancellationToken cancellationToken);

    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Existe algum lançamento cujo <see cref="Transaction.ReversalOfTransactionId"/> aponta para este?</summary>
    Task<bool> HasReversalAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>
    /// Para o subconjunto de <paramref name="transactionIds"/> que já foram estornados, mapeia
    /// o id do lançamento original para o id do lançamento de estorno correspondente.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetReversalMapAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken);
}
