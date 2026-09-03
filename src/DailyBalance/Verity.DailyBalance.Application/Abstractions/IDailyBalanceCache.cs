using Verity.DailyBalance.Application.DailyBalances.Dtos;

namespace Verity.DailyBalance.Application.Abstractions;

/// <summary>
/// Cache-aside para a consulta de saldo diário (ADR-006). Implementado com Redis; em caso de
/// indisponibilidade do Redis, a leitura cai para o PostgreSQL (fallback), com latência maior
/// mas sem indisponibilidade da consulta.
/// </summary>
public interface IDailyBalanceCache
{
    Task<DailyBalanceDto?> GetAsync(DateOnly businessDate, CancellationToken cancellationToken);

    Task SetAsync(DateOnly businessDate, DailyBalanceDto value, CancellationToken cancellationToken);

    Task InvalidateAsync(DateOnly businessDate, CancellationToken cancellationToken);
}
