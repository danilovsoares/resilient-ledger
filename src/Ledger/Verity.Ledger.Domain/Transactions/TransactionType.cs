namespace Verity.Ledger.Domain.Transactions;

/// <summary>
/// Tipo de lançamento no domínio do Ledger. Mantido separado do enum de contrato de integração
/// (<c>Verity.Shared.Contracts.IntegrationEvents.TransactionType</c>) para que o domínio não
/// dependa do formato publicado no broker.
/// </summary>
public enum TransactionType
{
    Credit = 1,
    Debit = 2
}
