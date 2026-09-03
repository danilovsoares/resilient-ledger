namespace Verity.Shared.Contracts.IntegrationEvents;

/// <summary>
/// Representação do tipo de lançamento no contrato de integração (payload de evento).
/// Independente do enum de domínio do Ledger para evitar acoplamento entre o modelo
/// interno de negócio e o contrato publicado no broker.
/// </summary>
public enum TransactionType
{
    Credit = 1,
    Debit = 2
}
