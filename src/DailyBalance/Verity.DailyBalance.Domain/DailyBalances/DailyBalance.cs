using Verity.DailyBalance.Domain.Exceptions;

namespace Verity.DailyBalance.Domain.DailyBalances;

/// <summary>
/// Projeção de saldo consolidado de uma data de negócio (UTC). Atualizada de forma incremental
/// pelo Daily Balance Worker a cada evento <c>TransactionRegisteredEvent</c> processado.
/// Eventos do escopo inicial são imutáveis e aditivos, portanto a ordem de aplicação não altera
/// o resultado final (ver ADR-004).
/// </summary>
public sealed class DailyBalance
{
    public DateOnly BusinessDate { get; private set; }
    public decimal TotalCredits { get; private set; }
    public decimal TotalDebits { get; private set; }
    public decimal Balance => TotalCredits - TotalDebits;
    public DateTimeOffset UpdatedAt { get; private set; }

    private DailyBalance() { }

    public static DailyBalance CreateEmpty(DateOnly businessDate) => new()
    {
        BusinessDate = businessDate,
        TotalCredits = 0m,
        TotalDebits = 0m,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Apply(TransactionKind kind, decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("O valor aplicado ao saldo diário deve ser positivo.");
        }

        switch (kind)
        {
            case TransactionKind.Credit:
                TotalCredits += amount;
                break;
            case TransactionKind.Debit:
                TotalDebits += amount;
                break;
            default:
                throw new DomainException($"Tipo de lançamento desconhecido: {kind}.");
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
