using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Application.DailyBalances.Dtos;

public sealed record DailyBalanceDto(
    DateOnly BusinessDate,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    DateTimeOffset? UpdatedAt)
{
    public static DailyBalanceDto FromDomain(DailyBalanceAggregate dailyBalance) => new(
        dailyBalance.BusinessDate,
        dailyBalance.TotalCredits,
        dailyBalance.TotalDebits,
        dailyBalance.Balance,
        dailyBalance.UpdatedAt);

    public static DailyBalanceDto Empty(DateOnly businessDate) => new(businessDate, 0m, 0m, 0m, null);
}
