using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Verity.DailyBalance.Application.Common;
using Verity.DailyBalance.Application.DailyBalances.Dtos;
using Verity.DailyBalance.Application.DailyBalances.Queries.GetDailyBalance;

namespace Verity.DailyBalance.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/daily-balances")]
[Produces("application/json")]
public sealed class DailyBalancesController(
    IQueryHandler<GetDailyBalanceQuery, DailyBalanceDto> getDailyBalanceHandler) : ControllerBase
{
    /// <summary>
    /// Consulta o saldo consolidado de uma data de negócio (UTC). Leitura otimizada por cache
    /// Redis (cache-aside); datas sem lançamentos retornam saldo zerado.
    /// </summary>
    [HttpGet("{date}")]
    [ProducesResponseType(typeof(DailyBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByDate(DateOnly date, CancellationToken cancellationToken)
    {
        var result = await getDailyBalanceHandler.HandleAsync(new GetDailyBalanceQuery(date), cancellationToken);
        return Ok(result);
    }
}
