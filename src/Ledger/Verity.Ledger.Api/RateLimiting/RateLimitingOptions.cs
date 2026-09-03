namespace Verity.Ledger.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 1;

    /// <summary>Limite mais apertado para o endpoint de login, para dificultar força bruta de senha.</summary>
    public int LoginPermitLimit { get; set; } = 10;
    public int LoginWindowSeconds { get; set; } = 60;
}
