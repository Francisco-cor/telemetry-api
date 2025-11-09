using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;
using Telemetry.Api.Infra;
using Telemetry.Api.Domain;
using Telemetry.Api.Api;
using Telemetry.Api.Middleware;
using Telemetry.Api.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Serilog mínimo y robusto, sin depender de appsettings.json
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
});

// Health checks: 'live' (self) y 'ready' (DB)
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<TelemDbReadyCheck>("db", tags: new[] { "ready" });

// --- conexión ---
var conn =
    builder.Configuration.GetConnectionString("Db")
    ?? builder.Configuration.GetConnectionString("Oracle");

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

// --- Función para escribir la respuesta de Health Checks en formato JSON ---
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

    var options = new JsonSerializerOptions
    {
        WriteIndented = true, // Para mejor legibilidad
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

app.UseSwagger();
app.UseSwaggerUI();

// --- Endpoints ---

// --- Health endpoints ---
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = WriteHealthJson
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = WriteHealthJson
});

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
            var details = new ProblemDetails
            {
                Title = "Invalid payload",
                Detail = string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))
            };
            return Results.BadRequest(details);
        }
        var events = batch.Events.Select(e => new TelemetryEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = e.Timestamp,
            Source = e.Source,
            MetricName = e.MetricName,
            MetricValue = e.MetricValue
        }).ToList();
        await db.Telemetry.AddRangeAsync(events);
        await db.SaveChangesAsync();
        return Results.Created($"/api/telemetry?inserted={events.Count}", new { inserted = events.Count });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to ingest telemetry batch.");
        return Results.Problem(
            title: "Ingestion failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("PostTelemetry")
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError);

// ---------- GET /api/telemetry ----------
app.MapGet("/api/telemetry", async (TelemDb db, string? source, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 100) =>
{
    page = Math.Max(page, 1);
    pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;
    var query = db.Telemetry.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(source))
        query = query.Where(t => t.Source == source);
    if (startDate.HasValue && endDate.HasValue)
        query = query.Where(t => t.Timestamp >= startDate.Value && t.Timestamp <= endDate.Value);
    var totalCount = await query.CountAsync();
    var items = await query
        .OrderByDescending(t => t.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new
        {
            t.Id,
            t.Timestamp,
            t.Source,
            t.MetricName,
            t.MetricValue
        })
        .ToListAsync();
    return Results.Ok(new { items, totalCount, page, pageSize });
})
.WithName("GetTelemetry")
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

public partial class Program { }