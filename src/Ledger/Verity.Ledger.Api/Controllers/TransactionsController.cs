using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Verity.Ledger.Api.Middleware;
using Verity.Ledger.Application.Common;
using Verity.Ledger.Application.Transactions.Commands.CancelTransaction;
using Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;
using Verity.Ledger.Application.Transactions.Dtos;
using Verity.Ledger.Application.Transactions.Queries.GetTransactionsByDate;
using Verity.Ledger.Domain.Transactions;
using Verity.Shared.Contracts.Correlation;

namespace Verity.Ledger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transactions")]
[Produces("application/json")]
public sealed class TransactionsController(
    ICommandHandler<RegisterTransactionCommand, RegisterTransactionResult> registerHandler,
    IQueryHandler<GetTransactionsByDateQuery, PagedResult<TransactionDto>> getByDateHandler,
    ICommandHandler<CancelTransactionCommand, CancelTransactionResult?> cancelHandler,
    IValidator<RegisterTransactionCommand> validator) : ControllerBase
{
    /// <summary>
    /// Registra um lançamento de crédito ou débito. Idempotente: repetir a mesma
    /// Idempotency-Key retorna o lançamento originalmente criado, sem duplicar efeito.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterTransactionRequest request,
        [FromHeader(Name = CorrelationHeaders.IdempotencyKey)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new RegisterTransactionCommand(
            request.Type,
            request.Amount,
            request.OccurredAt ?? DateTimeOffset.UtcNow,
            request.Description,
            idempotencyKey ?? string.Empty,
            HttpContext.GetCorrelationId());

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(BuildValidationProblem(validation));
        }

        var result = await registerHandler.HandleAsync(command, cancellationToken);

        return result.IsNewRegistration
            ? StatusCode(StatusCodes.Status201Created, result.Transaction)
            : Ok(result.Transaction);
    }

    private const int MaxPageSize = 10;

    /// <summary>Consulta paginada dos lançamentos de uma data de negócio (UTC) específica.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByDate(
        [FromQuery] DateOnly date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = MaxPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await getByDateHandler.HandleAsync(new GetTransactionsByDateQuery(date, page, pageSize), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Estorna um lançamento: registra um novo lançamento, de tipo oposto e mesmo valor, que
    /// zera o efeito do original no saldo. O lançamento original nunca é alterado ou removido —
    /// não existe edição de lançamentos neste domínio (ver docs/adr/004).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await cancelHandler.HandleAsync(new CancelTransactionCommand(id, HttpContext.GetCorrelationId()), cancellationToken);
        return result is null ? NotFound() : Ok(result.Reversal);
    }

    private static ModelStateDictionary BuildValidationProblem(FluentValidation.Results.ValidationResult validation)
    {
        var modelState = new ModelStateDictionary();
        foreach (var error in validation.Errors)
        {
            modelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return modelState;
    }
}

public sealed record RegisterTransactionRequest(
    TransactionType Type,
    decimal Amount,
    DateTimeOffset? OccurredAt,
    string? Description);
