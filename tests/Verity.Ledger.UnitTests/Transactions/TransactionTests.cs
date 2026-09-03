using FluentAssertions;
using Verity.Ledger.Domain.Exceptions;
using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.UnitTests.Transactions;

public class TransactionTests
{
    [Fact]
    public void Register_com_dados_validos_cria_lancamento_e_levanta_evento_de_dominio()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        var transaction = Transaction.Register(
            TransactionType.Credit,
            150.50m,
            occurredAt,
            "idem-key-1",
            "Venda balcão");

        transaction.Id.Should().NotBeEmpty();
        transaction.Type.Should().Be(TransactionType.Credit);
        transaction.Amount.Should().Be(150.50m);
        transaction.BusinessDate.Should().Be(new DateOnly(2026, 9, 2));
        transaction.IdempotencyKey.Should().Be("idem-key-1");

        var domainEvent = transaction.DomainEvents.OfType<TransactionRegisteredDomainEvent>().Single();
        domainEvent.TransactionId.Should().Be(transaction.Id);
        domainEvent.Amount.Should().Be(150.50m);
        domainEvent.BusinessDate.Should().Be(transaction.BusinessDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Register_com_valor_nao_positivo_lanca_DomainException(decimal amount)
    {
        var act = () => Transaction.Register(
            TransactionType.Debit,
            amount,
            DateTimeOffset.UtcNow,
            "idem-key",
            null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Register_sem_idempotency_key_lanca_DomainException()
    {
        var act = () => Transaction.Register(
            TransactionType.Credit,
            10m,
            DateTimeOffset.UtcNow,
            idempotencyKey: "   ",
            description: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void BusinessDate_e_derivada_do_instante_em_UTC()
    {
        // 23:30 em UTC-3 (Brasília) equivale a 02:30 do dia seguinte em UTC.
        var occurredAtLocal = new DateTimeOffset(2026, 9, 2, 23, 30, 0, TimeSpan.FromHours(-3));

        var transaction = Transaction.Register(TransactionType.Credit, 10m, occurredAtLocal, "k", null);

        transaction.BusinessDate.Should().Be(new DateOnly(2026, 9, 3));
    }

    [Theory]
    [InlineData(TransactionType.Credit, TransactionType.Debit)]
    [InlineData(TransactionType.Debit, TransactionType.Credit)]
    public void RegisterReversal_cria_lancamento_de_tipo_oposto_com_o_mesmo_valor(
        TransactionType originalType, TransactionType expectedReversalType)
    {
        var original = Transaction.Register(originalType, 150.50m, DateTimeOffset.UtcNow, "k", "Venda");

        var reversal = Transaction.RegisterReversal(original, "reversal-key");

        reversal.Id.Should().NotBe(original.Id);
        reversal.Type.Should().Be(expectedReversalType);
        reversal.Amount.Should().Be(original.Amount);
        reversal.ReversalOfTransactionId.Should().Be(original.Id);
    }

    [Fact]
    public void RegisterReversal_nao_usa_a_data_de_ocorrencia_do_original()
    {
        var original = Transaction.Register(
            TransactionType.Credit, 10m, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "k", null);

        var reversal = Transaction.RegisterReversal(original, "reversal-key");

        // O estorno corrige o saldo de hoje, não reescreve o saldo já consolidado de um dia
        // passado — ver Transaction.RegisterReversal.
        reversal.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        reversal.BusinessDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public void RegisterReversal_levanta_evento_de_dominio_como_qualquer_outro_lancamento()
    {
        var original = Transaction.Register(TransactionType.Credit, 10m, DateTimeOffset.UtcNow, "k", null);

        var reversal = Transaction.RegisterReversal(original, "reversal-key");

        var domainEvent = reversal.DomainEvents.OfType<TransactionRegisteredDomainEvent>().Single();
        domainEvent.TransactionId.Should().Be(reversal.Id);
        domainEvent.Type.Should().Be(reversal.Type);
        domainEvent.Amount.Should().Be(reversal.Amount);
    }
}
