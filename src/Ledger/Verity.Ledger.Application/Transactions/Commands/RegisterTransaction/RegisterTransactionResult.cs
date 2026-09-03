using Verity.Ledger.Application.Transactions.Dtos;

namespace Verity.Ledger.Application.Transactions.Commands.RegisterTransaction;

/// <summary>
/// <paramref name="IsNewRegistration"/> é falso quando a requisição foi resolvida por
/// replay de idempotência (a chave já existia) — nesse caso a Api deve responder 200 em vez de 201
/// e nenhum novo evento é publicado.
/// </summary>
public sealed record RegisterTransactionResult(TransactionDto Transaction, bool IsNewRegistration);
