using Microsoft.Extensions.DependencyInjection;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Application.DailyBalances.Queries.GetDailyBalance;

namespace Verity.DailyBalance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDailyBalanceApplication(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<ApplyTransactionCommand, ApplyTransactionResult>, ApplyTransactionHandler>();
        services.AddScoped<IQueryHandler<GetDailyBalanceQuery, DailyBalanceDto>, GetDailyBalanceHandler>();

        return services;
    }
}
