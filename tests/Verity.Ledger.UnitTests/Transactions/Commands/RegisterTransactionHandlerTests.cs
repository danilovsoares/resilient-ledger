using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;
using Verity.Ledger.Domain.Transactions;
using Verity.Shared.Contracts.IntegrationEvents;
using TransactionType = Verity.Ledger.Domain.Transactions.TransactionType;

namespace Verity.Ledger.UnitTests.Transactions.Commands;

public class RegisterTransactionHandlerTests
{
    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterTransactionHandler _handler;

    public RegisterTransactionHandlerTests()
    {
        _handler = new RegisterTransactionHandler(_repository, _outbox, _unitOfWork, Substitute.For<ILogger<RegisterTransactionHandler>>());
    }

    private static RegisterTransactionCommand BuildCommand(string idempotencyKey = "key-1") => new(
        TransactionType.Credit,
        100m,
        DateTimeOffset.UtcNow,
        "descrição",
        idempotencyKey,
        Guid.NewGuid());

    [Fact]
    public async Task Lancamento_novo_e_persistido_e_evento_e_enfileirado_na_outbox()
    {
        _repository.GetByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Transaction?)null);

        var command = BuildCommand();

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsNewRegistration.Should().BeTrue();
        _repository.Received(1).Add(Arg.Any<Transaction>());
        _outbox.Received(1).Enqueue(
            Arg.Is<TransactionRegisteredEvent>(e => e.Amount == command.Amount && e.CorrelationId == command.CorrelationId),
            command.CorrelationId,
            command.CorrelationId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reenvio_da_mesma_IdempotencyKey_nao_cria_novo_lancamento_nem_publica_evento()
    {
        var existing = Transaction.Register(TransactionType.Credit, 100m, DateTimeOffset.UtcNow, "key-1", null);
        _repository.GetByIdempotencyKeyAsync("key-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.HandleAsync(BuildCommand("key-1"), CancellationToken.None);

        result.IsNewRegistration.Should().BeFalse();
        result.Transaction.Id.Should().Be(existing.Id);
        _repository.DidNotReceive().Add(Arg.Any<Transaction>());
        _outbox.DidNotReceiveWithAnyArgs().Enqueue(default(TransactionRegisteredEvent)!, default, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Corrida_de_idempotencia_no_SaveChanges_e_tratada_como_replay()
    {
        var winner = Transaction.Register(TransactionType.Credit, 100m, DateTimeOffset.UtcNow, "key-1", null);

        _repository.GetByIdempotencyKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns((Transaction?)null, winner);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new IdempotencyConflictException("key-1"));

        var result = await _handler.HandleAsync(BuildCommand("key-1"), CancellationToken.None);

        result.IsNewRegistration.Should().BeFalse();
        result.Transaction.Id.Should().Be(winner.Id);
        await _repository.Received(2).GetByIdempotencyKeyAsync("key-1", Arg.Any<CancellationToken>());
    }
}
