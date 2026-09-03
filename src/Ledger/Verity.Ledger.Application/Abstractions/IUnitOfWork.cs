namespace Verity.Ledger.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Persiste todas as alterações rastreadas (lançamento + mensagem de outbox) em uma única
    /// transação de banco de dados. Lança <see cref="IdempotencyConflictException"/> se a
    /// chave de idempotência já existir (violação de índice único), permitindo ao handler
    /// tratar corrida entre requisições concorrentes com a mesma chave.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class IdempotencyConflictException(string idempotencyKey)
    : Exception($"Já existe um lançamento registrado com a chave de idempotência '{idempotencyKey}'.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
