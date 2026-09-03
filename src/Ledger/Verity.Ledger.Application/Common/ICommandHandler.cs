namespace Verity.Ledger.Application.Common;

/// <summary>
/// Abstração mínima para separar comandos (escrita) de queries (leitura) — CQRS pragmático,
/// sem um mediador genérico. Cada caso de uso tem um handler explícito e injetável.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
