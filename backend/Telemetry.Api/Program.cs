using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using FluentValidation;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

using Telemetry.Api.Api;
using Telemetry.Api.Domain;
using Telemetry.Api.Health;
using Telemetry.Api.Infra;
using Telemetry.Api.Middleware;
using Telemetry.Api.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Serilog cargado desde configuración
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateBootstrapLogger();

builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Telemetry API", Version = "v1" });
    c.OperationFilter<AddCorrelationHeaderOperationFilter>();
    c.OperationFilter<AddTooManyRequestsResponseOperationFilter>(); // ← nuevo
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<TelemDbReadyCheck>("db", tags: new[] { "ready" });

builder.Services.AddRateLimiter(options =>
{
    var permitLimit = builder.Configuration.GetValue<int>("RateLimiting:PermitLimit", 100);
    var windowMinutes = builder.Configuration.GetValue<int>("RateLimiting:WindowMinutes", 5);

    options.AddPolicy("fixed-per-ip", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        ctx.HttpContext.Response.Headers.RetryAfter = "60";

        var problem = new
        {
            type = "about:blank",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. See Retry-After header.",
            traceId = ctx.HttpContext.TraceIdentifier
        };
        await ctx.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), ct);
    };
});

// --- conexión ---
var conn = builder.Configuration.GetConnectionString("Db") ?? builder.Configuration.GetConnectionString("Oracle");
if (string.IsNullOrWhiteSpace(conn) && !builder.Environment.IsEnvironment("Testing"))
{
    Log.Fatal("No connection string 'Db'/'Oracle' found.");
    throw new InvalidOperationException("Connection string 'Db'/'Oracle' is not configured.");
}
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<TelemDb>(opts => opts.UseOracle(conn!));
}
builder.Services.AddScoped<IValidator<TelemetryIngestBatch>, TelemetryBatchValidator>();

var app = builder.Build();

static Task WriteHealthJson(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json; charset=utf-8";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            durationMs = (int)e.Value.Duration.TotalMilliseconds,
            error = e.Value.Exception?.Message
        })
    };
    var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, options));
}

// --- Pipeline de Middleware ---
app.UseCorrelationId();
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms (corrId: {CorrelationId})";
    opts.EnrichDiagnosticContext = (diag, http) =>
    {
        var corr = http.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString() ?? http.TraceIdentifier;
        diag.Set("CorrelationId", corr);
        diag.Set("ClientIP", http.Connection.RemoteIpAddress?.ToString());
        diag.Set("QueryString", http.Request.QueryString.HasValue ? http.Request.QueryString.Value : "");
        diag.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
    };
});
app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI();

// --- Endpoints ---
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = s_livePredicate, ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = s_readyPredicate, ResponseWriter = WriteHealthJson });

// ---------- POST /api/telemetry ----------
app.MapPost("/api/telemetry", async (
    TelemDb db,
    IValidator<TelemetryIngestBatch> validator,
    [FromBody] TelemetryIngestBatch batch
) =>
{
    try
    {
        var validation = await validator.ValidateAsync(batch);
        if (!validation.IsValid)
        {
            var sb = new StringBuilder();
            foreach (var e in validation.Errors)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(e.PropertyName).Append(": ").Append(e.ErrorMessage);
            }
            var details = new ProblemDetails { Title = "Invalid payload", Detail = sb.ToString() };
            return Results.BadRequest(details);
        }
        // Usar GUIDs secuenciales para evitar fragmentación
        var events = batch.Events.Select(e => new TelemetryEvent
        {
            Id = SequentialGuidGenerator.Create(),
            Timestamp = e.Timestamp,
            Source = e.Source,
            MetricName = e.MetricName,
            MetricValue = e.MetricValue
        });
        await db.Telemetry.AddRangeAsync(events);
        await db.SaveChangesAsync();
        var count = batch.Events.Count;
        return Results.Created($"/api/telemetry?inserted={count}", new { inserted = count });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to ingest telemetry batch.");
        return Results.Problem(title: "Ingestion failed", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("PostTelemetry")
.WithMetadata(new RequestSizeLimitAttribute(256 * 1024))
.RequireRateLimiting("fixed-per-ip")
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError);


// ---------- GET /api/telemetry ----------
app.MapGet("/api/telemetry", async (
    TelemDb db,
    string? source,
    DateTime? startDate,
    DateTime? endDate,
    DateTime? afterTimestamp,
    Guid? afterId,
    int pageSize = 100
) =>
{
    pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;
    var query = db.Telemetry.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(source))
        query = query.Where(t => t.Source == source);

    if (startDate.HasValue)
        query = query.Where(t => t.Timestamp >= startDate.Value);

    if (endDate.HasValue)
        query = query.Where(t => t.Timestamp <= endDate.Value);

    // Keyset pagination (afterTimestamp + afterId para desempate)
    if (afterTimestamp.HasValue && afterId.HasValue)
    {
        query = query.Where(t => t.Timestamp < afterTimestamp.Value ||
                               (t.Timestamp == afterTimestamp.Value && t.Id.CompareTo(afterId.Value) < 0));
    }

    var items = await query
        .OrderByDescending(t => t.Timestamp)
        .ThenByDescending(t => t.Id)
        .Take(pageSize + 1)
        .Select(t => new { t.Id, t.Timestamp, t.Source, t.MetricName, t.MetricValue })
        .ToListAsync();

    var hasNext = items.Count > pageSize;
    if (hasNext) items.RemoveAt(pageSize);

    var lastItem = items.LastOrDefault();
    var nextTimestamp = lastItem?.Timestamp;
    var nextId = lastItem?.Id;

    return Results.Ok(new { items, hasNext, nextTimestamp, nextId, pageSize });
})
.WithName("GetTelemetry")
.RequireRateLimiting("fixed-per-ip")
.Produces(StatusCodes.Status200OK);

// --- Arranque y Migraciones ---
try
{
    if (!app.Environment.IsEnvironment("Testing"))
    {
        Log.Information("Applying EF Core migrations...");
        using var scope = app.Services.CreateScope();
        var dbCtx = scope.ServiceProvider.GetRequiredService<TelemDb>();
        dbCtx.Database.Migrate();
        Log.Information("EF Core migrations applied.");
    }
    Log.Information("Starting Telemetry API");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Startup or migration failed.");
}
finally
{
    Log.CloseAndFlush();
}


public partial class Program
{
    // Cached delegates – allocated once at startup, reused on every health check poll.
    private static readonly Func<HealthCheckRegistration, bool> s_livePredicate =
        r => r.Tags.Contains("live");
    private static readonly Func<HealthCheckRegistration, bool> s_readyPredicate =
        r => r.Tags.Contains("ready");
}
