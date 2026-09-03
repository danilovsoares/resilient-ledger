namespace Verity.DailyBalance.Domain.DailyBalances;

/// <summary>
/// Efeito de um lançamento sobre o saldo diário. Mantido separado do enum de contrato de
/// integração para que o domínio da projeção não dependa do formato publicado no broker.
/// </summary>
public enum TransactionKind
{
    Credit = 1,
    Debit = 2
}
