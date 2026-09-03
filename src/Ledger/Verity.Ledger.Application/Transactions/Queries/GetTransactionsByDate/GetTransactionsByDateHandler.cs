using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Dtos;

namespace Verity.Ledger.Application.Transactions.Queries.GetTransactionsByDate;

public sealed class GetTransactionsByDateHandler(ITransactionRepository repository)
    : IQueryHandler<GetTransactionsByDateQuery, PagedResult<TransactionDto>>
{
    public async Task<PagedResult<TransactionDto>> HandleAsync(GetTransactionsByDateQuery query, CancellationToken cancellationToken)
    {
        var (transactions, totalCount) = await repository.GetByBusinessDatePagedAsync(
            query.BusinessDate, query.Page, query.PageSize, cancellationToken);

        // O estorno pode ter sido registrado em outro dia de negócio (ver Transaction.RegisterReversal),
        // então "este lançamento já foi estornado?" não pode ser respondido só com os dados desta página.
        var reversalMap = await repository.GetReversalMapAsync(
            transactions.Select(t => t.Id).ToList(),
            cancellationToken);

        var items = transactions
            .Select(t => TransactionDto.FromDomain(t, reversalMap.TryGetValue(t.Id, out var reversalId) ? reversalId : null))
            .ToList();

        return new PagedResult<TransactionDto>(items, query.Page, query.PageSize, totalCount);
    }
}
