using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Serilog;
using Serilog.Formatting.Compact;
using Telemetry.Api.Infra;
using Telemetry.Api.Domain;
using Telemetry.Api.Api;

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
builder.Services.AddSwaggerGen();

// Verificación explícita de la cadena de conexión
var connectionString = builder.Configuration.GetConnectionString("Db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Log.Fatal("La cadena de conexión 'Db' no se ha encontrado. Revisa las variables de entorno en docker-compose.yml.");
    throw new InvalidOperationException("Connection string 'Db' is not configured.");
}
builder.Services.AddDbContext<TelemDb>(opts => opts.UseOracle(connectionString));

builder.Services.AddScoped<IValidator<TelemetryIngestBatch>, TelemetryBatchValidator>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/db", async (TelemDb db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM DUAL");
        return Results.Ok(new { db = "ok" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "El health check de la base de datos ha fallado.");
        return Results.Problem(title: "DB check failed", detail: "Database is not reachable.", statusCode: 500);
    }
});

// ---------- POST /api/telemetry ----------
app.MapPost("/api/telemetry", async (TelemDb db, IValidator<TelemetryIngestBatch> validator, TelemetryIngestBatch batch) =>
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
})
.WithName("PostTelemetry")
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest);

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

try
{
    Log.Information("Iniciando la aplicación Telemetry API");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación ha fallado al iniciar.");
}
finally
{
    Log.CloseAndFlush();
}