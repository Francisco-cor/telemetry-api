using System.Diagnostics;

namespace Telemetry.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        // 1) Leer o generar correlation id
        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var corr))
        {
            corr = Guid.NewGuid().ToString("n");
            ctx.Request.Headers[HeaderName] = corr;
        }

        // 2) Propagarlo a la respuesta y a TraceIdentifier
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[HeaderName] = corr.ToString();
            return Task.CompletedTask;
        });
        ctx.TraceIdentifier = corr!; // útil para logs/trazas

        await _next(ctx);
    }
}

// Helper para registrar el middleware
public static class CorrelationIdExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
