namespace Verity.Shared.Contracts.Correlation;

/// <summary>
/// Nomes de cabeçalhos HTTP e chaves de metadados de mensageria usados para propagar
/// identidade de correlação ponta a ponta (API -> broker -> worker).
/// </summary>
public static class CorrelationHeaders
{
    public const string CorrelationId = "X-Correlation-ID";
    public const string IdempotencyKey = "Idempotency-Key";
}
