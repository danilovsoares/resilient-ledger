using FluentAssertions;
using Verity.DailyBalance.Domain.Exceptions;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;
using Verity.DailyBalance.Domain.DailyBalances;

namespace Verity.DailyBalance.UnitTests.DailyBalances;

public class DailyBalanceTests
{
    [Fact]
    public void CreateEmpty_inicia_totais_zerados()
    {
        var balance = DailyBalanceAggregate.CreateEmpty(new DateOnly(2026, 9, 2));

        balance.TotalCredits.Should().Be(0m);
        balance.TotalDebits.Should().Be(0m);
        balance.Balance.Should().Be(0m);
    }

    [Fact]
    public void Apply_credito_e_debito_calcula_saldo_corretamente()
    {
        var balance = DailyBalanceAggregate.CreateEmpty(new DateOnly(2026, 9, 2));

        balance.Apply(TransactionKind.Credit, 150.50m);
        balance.Apply(TransactionKind.Debit, 40m);

        balance.TotalCredits.Should().Be(150.50m);
        balance.TotalDebits.Should().Be(40m);
        balance.Balance.Should().Be(110.50m);
    }

    [Fact]
    public void Ordem_de_aplicacao_dos_lancamentos_nao_altera_o_saldo_final()
    {
        var creditFirst = DailyBalanceAggregate.CreateEmpty(new DateOnly(2026, 9, 2));
        creditFirst.Apply(TransactionKind.Credit, 100m);
        creditFirst.Apply(TransactionKind.Debit, 30m);

        var debitFirst = DailyBalanceAggregate.CreateEmpty(new DateOnly(2026, 9, 2));
        debitFirst.Apply(TransactionKind.Debit, 30m);
        debitFirst.Apply(TransactionKind.Credit, 100m);

        creditFirst.Balance.Should().Be(debitFirst.Balance);
        creditFirst.TotalCredits.Should().Be(debitFirst.TotalCredits);
        creditFirst.TotalDebits.Should().Be(debitFirst.TotalDebits);
    }

    [Fact]
    public void Apply_com_valor_nao_positivo_lanca_DomainException()
    {
        var balance = DailyBalanceAggregate.CreateEmpty(new DateOnly(2026, 9, 2));

        var act = () => balance.Apply(TransactionKind.Credit, 0m);

        act.Should().Throw<DomainException>();
    }
}
