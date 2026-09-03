using FluentAssertions;
using NSubstitute;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Transactions.Queries.GetTransactionsByDate;
using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.UnitTests.Transactions.Queries;

public class GetTransactionsByDateHandlerTests
{
    [Fact]
    public async Task Retorna_lancamentos_da_data_informada_mapeados_para_dto()
    {
        var repository = Substitute.For<ITransactionRepository>();
        var businessDate = new DateOnly(2026, 9, 2);
        var transaction = Transaction.Register(TransactionType.Debit, 25m, DateTimeOffset.UtcNow, "k", "desc");

        repository.GetByBusinessDatePagedAsync(businessDate, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Transaction> { transaction }, 1));
        repository.GetReversalMapAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, Guid>());

        var handler = new GetTransactionsByDateHandler(repository);

        var result = await handler.HandleAsync(new GetTransactionsByDateQuery(businessDate), CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(dto => dto.Id == transaction.Id && dto.Amount == 25m && dto.ReversedByTransactionId == null);
    }

    [Fact]
    public async Task Lancamento_ja_estornado_traz_o_id_do_estorno_no_dto()
    {
        var repository = Substitute.For<ITransactionRepository>();
        var businessDate = new DateOnly(2026, 9, 2);
        var transaction = Transaction.Register(TransactionType.Debit, 25m, DateTimeOffset.UtcNow, "k", "desc");
        var reversalId = Guid.NewGuid();

        repository.GetByBusinessDatePagedAsync(businessDate, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Transaction> { transaction }, 1));
        repository.GetReversalMapAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, Guid> { [transaction.Id] = reversalId });

        var handler = new GetTransactionsByDateHandler(repository);

        var result = await handler.HandleAsync(new GetTransactionsByDateQuery(businessDate), CancellationToken.None);

        result.Items.Should().ContainSingle(dto => dto.Id == transaction.Id && dto.ReversedByTransactionId == reversalId);
    }
}
