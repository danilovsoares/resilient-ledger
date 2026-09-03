namespace Verity.Ledger.Domain.Exceptions;

/// <summary>
/// Lançada quando uma invariante de negócio do domínio de Ledger é violada.
/// A camada de Api traduz esta exceção em uma resposta 400 (Problem Details).
/// </summary>
public sealed class DomainException(string message) : Exception(message);
