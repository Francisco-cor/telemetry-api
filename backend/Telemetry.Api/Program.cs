using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Telemetry.Api.Infra;
using Telemetry.Api.Domain;
using Telemetry.Api.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Lee la cadena de conexión "Db" proporcionada por la variable de entorno.
var connectionString = builder.Configuration.GetConnectionString("Db");
builder.Services.AddDbContext<TelemDb>(opts => opts.UseOracle(connectionString));

builder.Services.AddScoped<IValidator<TelemetryIngestBatch>, TelemetryBatchValidator>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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

app.Run();