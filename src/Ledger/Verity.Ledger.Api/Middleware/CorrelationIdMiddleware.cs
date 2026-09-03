using Serilog.Context;
using Verity.Shared.Contracts.Correlation;

namespace Verity.Ledger.Api.Middleware;

/// <summary>
/// Lê o cabeçalho X-Correlation-ID do request; se ausente ou inválido, gera um novo GUID.
/// Publica o valor em <see cref="HttpContext.Items"/> (para uso pelos controllers/handlers),
/// no contexto de log do Serilog e no cabeçalho de resposta — permitindo rastrear a mesma
/// jornada do request HTTP até o consumidor do evento (ver docs/observability.md).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HttpContextItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[HttpContextItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId.ToString();
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static Guid ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeaders.CorrelationId, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed))
        {
            return parsed;
        }

        return Guid.NewGuid();
    }
}

public static class HttpContextCorrelationExtensions
{
    public static Guid GetCorrelationId(this HttpContext context) =>
        context.Items[CorrelationIdMiddleware.HttpContextItemKey] as Guid? ?? Guid.NewGuid();
}
