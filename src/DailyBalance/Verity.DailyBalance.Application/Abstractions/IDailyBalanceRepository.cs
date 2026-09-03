using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Application.Abstractions;

public interface IDailyBalanceRepository
{
    Task<DailyBalanceAggregate?> GetByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken);

    /// <summary>Rastreia o agregado para persistência via <see cref="IUnitOfWork"/> (insert ou update).</summary>
    void Upsert(DailyBalanceAggregate dailyBalance, bool isNew);
}
