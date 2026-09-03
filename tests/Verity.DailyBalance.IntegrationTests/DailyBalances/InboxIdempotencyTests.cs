using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;
using Verity.DailyBalance.Domain.DailyBalances;
using Verity.DailyBalance.Infrastructure.Persistence;
using Verity.DailyBalance.IntegrationTests.Infrastructure;

namespace Verity.DailyBalance.IntegrationTests.DailyBalances;

/// <summary>
/// Prova, contra PostgreSQL real, que reentregas do mesmo evento (padrão Inbox) não duplicam
/// o efeito no saldo — nem em sequência, nem em corrida concorrente (ver ADR-003, ADR-004).
/// </summary>
[Collection(DailyBalanceIntegrationCollection.Name)]
public sealed class InboxIdempotencyTests : IAsyncLifetime
{
    private readonly DailyBalanceApiFactory _factory = new();

    public Task InitializeAsync() => _factory.InitializeAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<ApplyTransactionResult> ApplyAsync(ApplyTransactionCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyTransactionCommand, ApplyTransactionResult>>();
        return await handler.HandleAsync(command, CancellationToken.None);
    }

    [Fact]
    public async Task Reentrega_sequencial_do_mesmo_EventId_e_no_op_e_nao_duplica_o_saldo()
    {
        var businessDate = new DateOnly(2026, 4, 1);
        var command = new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Credit, 75m, businessDate, Guid.NewGuid());

        var first = await ApplyAsync(command);
        var second = await ApplyAsync(command); // reentrega: mesmo EventId

        first.WasAlreadyProcessed.Should().BeFalse();
        second.WasAlreadyProcessed.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var balance = await dbContext.DailyBalances.SingleAsync(b => b.BusinessDate == businessDate);
        balance.TotalCredits.Should().Be(75m, "a reentrega não deve somar o valor uma segunda vez");
    }

    [Fact]
    public async Task Corrida_concorrente_para_o_mesmo_EventId_aplica_o_efeito_uma_unica_vez()
    {
        var businessDate = new DateOnly(2026, 4, 2);
        var command = new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Credit, 50m, businessDate, Guid.NewGuid());

        var results = await Task.WhenAll(ApplyAsync(command), ApplyAsync(command));

        results.Count(r => !r.WasAlreadyProcessed).Should().Be(1, "apenas uma das duas transações concorrentes deve vencer a corrida");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var balance = await dbContext.DailyBalances.SingleAsync(b => b.BusinessDate == businessDate);
        balance.TotalCredits.Should().Be(50m);
    }

    [Fact]
    public async Task CorrelationId_do_evento_e_persistido_no_registro_da_Inbox()
    {
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var command = new ApplyTransactionCommand(eventId, Guid.NewGuid(), TransactionKind.Debit, 10m, new DateOnly(2026, 4, 3), correlationId);

        await ApplyAsync(command);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var processedMessage = await dbContext.ProcessedMessages.SingleAsync(p => p.EventId == eventId);
        processedMessage.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task Ordem_de_chegada_dos_eventos_nao_altera_o_saldo_final_aditivo()
    {
        var dateA = new DateOnly(2026, 4, 4);
        var dateB = new DateOnly(2026, 4, 5);

        // Data A: crédito antes de débito. Data B: débito antes de crédito.
        await ApplyAsync(new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Credit, 100m, dateA, Guid.NewGuid()));
        await ApplyAsync(new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Debit, 30m, dateA, Guid.NewGuid()));

        await ApplyAsync(new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Debit, 30m, dateB, Guid.NewGuid()));
        await ApplyAsync(new ApplyTransactionCommand(Guid.NewGuid(), Guid.NewGuid(), TransactionKind.Credit, 100m, dateB, Guid.NewGuid()));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        var balanceA = await dbContext.DailyBalances.SingleAsync(b => b.BusinessDate == dateA);
        var balanceB = await dbContext.DailyBalances.SingleAsync(b => b.BusinessDate == dateB);

        balanceA.Balance.Should().Be(balanceB.Balance);
        balanceA.Balance.Should().Be(70m);
    }
}
