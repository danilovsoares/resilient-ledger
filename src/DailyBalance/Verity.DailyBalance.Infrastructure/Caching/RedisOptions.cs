namespace Verity.DailyBalance.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>TTL da entrada de cache do saldo diário (ADR-006).</summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromSeconds(30);
}
