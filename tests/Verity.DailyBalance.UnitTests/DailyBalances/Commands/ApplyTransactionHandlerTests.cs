using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Domain.DailyBalances;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.UnitTests.DailyBalances.Commands;

public class ApplyTransactionHandlerTests
{
    private readonly IDailyBalanceRepository _repository = Substitute.For<IDailyBalanceRepository>();
    private readonly IProcessedMessageStore _processedMessages = Substitute.For<IProcessedMessageStore>();
    private readonly IDailyBalanceCache _cache = Substitute.For<IDailyBalanceCache>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ApplyTransactionHandler _handler;

    public ApplyTransactionHandlerTests()
    {
        _handler = new ApplyTransactionHandler(
            _repository, _processedMessages, _cache, _unitOfWork, Substitute.For<ILogger<ApplyTransactionHandler>>());
    }

    private static ApplyTransactionCommand BuildCommand(Guid eventId) => new(
        eventId,
        Guid.NewGuid(),
        TransactionKind.Credit,
        100m,
        new DateOnly(2026, 9, 2),
        Guid.NewGuid());

    [Fact]
    public async Task Evento_novo_atualiza_projecao_marca_inbox_e_invalida_cache()
    {
        var command = BuildCommand(Guid.NewGuid());
        _processedMessages.IsProcessedAsync(command.EventId, Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetByBusinessDateAsync(command.BusinessDate, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceAggregate?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.WasAlreadyProcessed.Should().BeFalse();
        _repository.Received(1).Upsert(Arg.Is<DailyBalanceAggregate>(b => b.TotalCredits == 100m), isNew: true);
        _processedMessages.Received(1).MarkProcessed(command.EventId, Arg.Any<string>(), command.CorrelationId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidateAsync(command.BusinessDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evento_ja_processado_e_ignorado_sem_tocar_na_projecao()
    {
        var command = BuildCommand(Guid.NewGuid());
        _processedMessages.IsProcessedAsync(command.EventId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.WasAlreadyProcessed.Should().BeTrue();
        await _repository.DidNotReceive().GetByBusinessDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().InvalidateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Corrida_de_deduplicacao_no_SaveChanges_e_tratada_como_no_op()
    {
        var command = BuildCommand(Guid.NewGuid());
        _processedMessages.IsProcessedAsync(command.EventId, Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetByBusinessDateAsync(command.BusinessDate, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceAggregate?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new DuplicateEventException(command.EventId));

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.WasAlreadyProcessed.Should().BeTrue();
        await _cache.DidNotReceive().InvalidateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}
