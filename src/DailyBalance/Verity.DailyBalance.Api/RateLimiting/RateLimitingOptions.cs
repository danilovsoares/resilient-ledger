namespace Verity.DailyBalance.Api.RateLimiting;

/// <summary>
/// Limite padrão de 300 req/s por IP: acima do alvo de carga de 50 RPS do NFR (ver
/// docs/non-functional-requirements.md) para não interferir no teste k6, mas ainda assim
/// protegendo a Api pública contra abuso.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 300;
    public int WindowSeconds { get; set; } = 1;
}
