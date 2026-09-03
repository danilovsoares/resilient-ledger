using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Transactions.Commands.CancelTransaction;
using Verity.Ledger.Domain.Exceptions;
using Verity.Ledger.Domain.Transactions;
using ContractTransactionType = Verity.Shared.Contracts.IntegrationEvents.TransactionType;
using TransactionRegisteredEvent = Verity.Shared.Contracts.IntegrationEvents.TransactionRegisteredEvent;

namespace Verity.Ledger.UnitTests.Transactions.Commands;

public class CancelTransactionHandlerTests
{
    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CancelTransactionHandler _handler;

    public CancelTransactionHandlerTests()
    {
        _handler = new CancelTransactionHandler(_repository, _outbox, _unitOfWork, Substitute.For<ILogger<CancelTransactionHandler>>());
    }

    [Fact]
    public async Task Lancamento_inexistente_retorna_null()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Transaction?)null);

        var result = await _handler.HandleAsync(new CancelTransactionCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Lancamento_ja_estornado_lanca_DomainException()
    {
        var original = Transaction.Register(TransactionType.Credit, 100m, DateTimeOffset.UtcNow, "k", null);
        _repository.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        _repository.HasReversalAsync(original.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.HandleAsync(new CancelTransactionCommand(original.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        _repository.DidNotReceive().Add(Arg.Any<Transaction>());
    }

    [Fact]
    public async Task Cancelamento_valido_persiste_o_estorno_e_publica_o_evento_na_outbox()
    {
        var original = Transaction.Register(TransactionType.Credit, 100m, DateTimeOffset.UtcNow, "k", "Venda");
        var correlationId = Guid.NewGuid();
        _repository.GetByIdAsync(original.Id, Arg.Any<CancellationToken>()).Returns(original);
        _repository.HasReversalAsync(original.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(new CancelTransactionCommand(original.Id, correlationId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Reversal.Type.Should().Be(TransactionType.Debit);
        result.Reversal.Amount.Should().Be(100m);
        result.Reversal.ReversalOfTransactionId.Should().Be(original.Id);

        _repository.Received(1).Add(Arg.Is<Transaction>(t => t.ReversalOfTransactionId == original.Id));
        _outbox.Received(1).Enqueue(
            Arg.Is<TransactionRegisteredEvent>(e => e.Type == ContractTransactionType.Debit && e.Amount == 100m),
            correlationId,
            correlationId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
