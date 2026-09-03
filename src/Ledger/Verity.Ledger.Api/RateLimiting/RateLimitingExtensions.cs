using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Verity.Ledger.Api.RateLimiting;

/// <summary>
/// Rate limiting global por janela fixa, particionado por endereço IP remoto. Protege a Api
/// pública de abuso; o limite é configurável por ambiente (ver docs/security.md).
/// </summary>
public static class RateLimitingExtensions
{
    public const string PolicyName = "fixed-window";
    public const string LoginPolicyName = "login-fixed-window";

    public static IServiceCollection AddLedgerRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                }));

            // Política dedicada e mais apertada para o login: o alvo de 100 req/s do restante
            // da Api é permissivo demais para tentativas de senha.
            limiter.AddPolicy(LoginPolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.LoginPermitLimit,
                    Window = TimeSpan.FromSeconds(options.LoginWindowSeconds),
                    QueueLimit = 0
                }));
        });

        return services;
    }
}
