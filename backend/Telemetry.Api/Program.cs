using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Serilog;
using Serilog.Formatting.Compact;
using Telemetry.Api.Infra;
using Telemetry.Api.Domain;
using Telemetry.Api.Api;
using Telemetry.Api.Middleware;
using Telemetry.Api.Swagger;
using Telemetry.Api.Health;
using Telemetry.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Serilog mínimo y robusto
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Telemetry API", Version = "v1" });
    c.OperationFilter<AddCorrelationHeaderOperationFilter>();
    c.OperationFilter<AddTooManyRequestsResponseOperationFilter>();
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<TelemDbReadyCheck>("db", tags: new[] { "ready" });

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("fixed-per-ip", httpContext =>
    {
        // TODO: Use ForwardedHeaders for real IP behind proxies
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(5),
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

// --- Services & DB ---
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
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(); // Required for IExceptionHandler

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

// --- Pipeline ---
app.UseExceptionHandler(); // Global Exception Handler
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
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live"), ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready"), ResponseWriter = WriteHealthJson });

app.MapTelemetryEndpoints();

// --- Startup & Migrations ---
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

public partial class Program { }
