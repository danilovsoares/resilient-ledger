using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Application.DailyBalances.Queries.GetDailyBalance;
using Verity.DailyBalance.Domain.DailyBalances;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.UnitTests.DailyBalances.Queries;

public class GetDailyBalanceHandlerTests
{
    private readonly IDailyBalanceCache _cache = Substitute.For<IDailyBalanceCache>();
    private readonly IDailyBalanceRepository _repository = Substitute.For<IDailyBalanceRepository>();
    private readonly GetDailyBalanceHandler _handler;
    private readonly DateOnly _businessDate = new(2026, 9, 2);

    public GetDailyBalanceHandlerTests()
    {
        _handler = new GetDailyBalanceHandler(_cache, _repository, Substitute.For<ILogger<GetDailyBalanceHandler>>());
    }

    [Fact]
    public async Task Cache_hit_retorna_direto_do_Redis_sem_consultar_o_banco()
    {
        var cached = new DailyBalanceDto(_businessDate, 100m, 20m, 80m, DateTimeOffset.UtcNow);
        _cache.GetAsync(_businessDate, Arg.Any<CancellationToken>()).Returns(cached);

        var result = await _handler.HandleAsync(new GetDailyBalanceQuery(_businessDate), CancellationToken.None);

        result.Should().Be(cached);
        await _repository.DidNotReceive().GetByBusinessDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cache_miss_consulta_o_banco_e_repopula_o_cache()
    {
        _cache.GetAsync(_businessDate, Arg.Any<CancellationToken>()).Returns((DailyBalanceDto?)null);
        var balance = DailyBalanceAggregate.CreateEmpty(_businessDate);
        balance.Apply(TransactionKind.Credit, 50m);
        _repository.GetByBusinessDateAsync(_businessDate, Arg.Any<CancellationToken>()).Returns(balance);

        var result = await _handler.HandleAsync(new GetDailyBalanceQuery(_businessDate), CancellationToken.None);

        result.Balance.Should().Be(50m);
        await _cache.Received(1).SetAsync(_businessDate, Arg.Is<DailyBalanceDto>(d => d.Balance == 50m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Data_sem_lancamentos_retorna_saldo_zerado_em_vez_de_erro()
    {
        _cache.GetAsync(_businessDate, Arg.Any<CancellationToken>()).Returns((DailyBalanceDto?)null);
        _repository.GetByBusinessDateAsync(_businessDate, Arg.Any<CancellationToken>()).Returns((DailyBalanceAggregate?)null);

        var result = await _handler.HandleAsync(new GetDailyBalanceQuery(_businessDate), CancellationToken.None);

        result.Balance.Should().Be(0m);
        result.UpdatedAt.Should().BeNull();
    }
}
