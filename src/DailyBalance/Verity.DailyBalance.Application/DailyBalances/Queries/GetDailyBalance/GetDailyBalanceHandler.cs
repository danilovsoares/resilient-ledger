using Microsoft.Extensions.Logging;
using Verity.DailyBalance.Application.Abstractions;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Dtos;

namespace Verity.DailyBalance.Application.DailyBalances.Queries.GetDailyBalance;

/// <summary>
/// Consulta cache-aside (ADR-006): tenta o Redis primeiro; em caso de cache miss, consulta o
/// PostgreSQL e repopula o cache. Datas sem lançamentos retornam saldo zerado, não 404.
/// </summary>
public sealed class GetDailyBalanceHandler(
    IDailyBalanceCache cache,
    IDailyBalanceRepository repository,
    ILogger<GetDailyBalanceHandler> logger)
    : IQueryHandler<GetDailyBalanceQuery, DailyBalanceDto>
{
    public async Task<DailyBalanceDto> HandleAsync(GetDailyBalanceQuery query, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(query.BusinessDate, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        logger.LogDebug("Cache miss para saldo diário de {BusinessDate}; consultando PostgreSQL", query.BusinessDate);

        var dailyBalance = await repository.GetByBusinessDateAsync(query.BusinessDate, cancellationToken);
        var dto = dailyBalance is null
            ? DailyBalanceDto.Empty(query.BusinessDate)
            : DailyBalanceDto.FromDomain(dailyBalance);

        await cache.SetAsync(query.BusinessDate, dto, cancellationToken);

        return dto;
    }
}
