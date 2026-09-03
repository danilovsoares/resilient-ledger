using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Application.Auth.Commands.Login;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Commands.CancelTransaction;
using Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;
using Verity.Ledger.Application.Transactions.Dtos;
using Verity.Ledger.Application.Transactions.Queries.GetTransactionsByDate;

namespace Verity.Ledger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<RegisterTransactionCommand, RegisterTransactionResult>, RegisterTransactionHandler>();
        services.AddScoped<IQueryHandler<GetTransactionsByDateQuery, PagedResult<TransactionDto>>, GetTransactionsByDateHandler>();
        services.AddScoped<ICommandHandler<CancelTransactionCommand, CancelTransactionResult?>, CancelTransactionHandler>();
        services.AddScoped<ICommandHandler<LoginCommand, LoginResult?>, LoginHandler>();
        services.AddValidatorsFromAssemblyContaining<RegisterTransactionValidator>();

        return services;
    }
}
