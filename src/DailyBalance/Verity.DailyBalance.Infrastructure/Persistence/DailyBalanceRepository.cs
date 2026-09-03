using Microsoft.EntityFrameworkCore;
using Verity.DailyBalance.Application.Abstractions;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Infrastructure.Persistence;

public sealed class DailyBalanceRepository(DailyBalanceDbContext dbContext) : IDailyBalanceRepository
{
    // AsNoTracking mesmo no caminho de escrita: seguro porque Upsert sempre chama Update()
    // explicitamente para reanexar a entidade antes de salvar (ver abaixo), e evita o custo de
    // rastreamento de mudanças em toda consulta feita pelo caminho de leitura (cache miss).
    public Task<DailyBalanceAggregate?> GetByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
        dbContext.DailyBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BusinessDate == businessDate, cancellationToken);

    public void Upsert(DailyBalanceAggregate dailyBalance, bool isNew)
    {
        if (isNew)
        {
            dbContext.DailyBalances.Add(dailyBalance);
        }
        else
        {
            dbContext.DailyBalances.Update(dailyBalance);
        }
    }
}
