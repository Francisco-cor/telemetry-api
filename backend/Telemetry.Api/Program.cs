using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using FluentValidation;
using FluentValidation.Results;
using Telemetry.Api.Infra;
using Telemetry.Api.Domain;
using Telemetry.Api.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<TelemDb>(opts =>
    opts.UseOracle(builder.Configuration.GetConnectionString("Oracle")));

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddScoped<IValidator<TelemetryIngestBatch>, TelemetryBatchValidator>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/telemetry", async Task<Results<BadRequest<ProblemDetails>, Created>>
    (TelemDb db, IValidator<TelemetryIngestBatch> validator, TelemetryIngestBatch batch) =>
{
    ValidationResult vr = await validator.ValidateAsync(batch);
    if (!vr.IsValid)
    {
        var pd = new ProblemDetails
        {
            Title = "Invalid payload",
            Detail = string.Join("; ", vr.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))
        };
        return TypedResults.BadRequest(pd);
    }

    var events = batch.Events.Select(dto => new TelemetryEvent
    {
        Id = Guid.NewGuid(),
        Timestamp = dto.Timestamp,
        Source = dto.Source,
        MetricName = dto.MetricName,
        MetricValue = dto.MetricValue
    }).ToList();

    await db.Telemetry.AddRangeAsync(events);
    await db.SaveChangesAsync();

    return TypedResults.Created($"/api/telemetry?count={events.Count}");
})
.Produces(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet("/api/telemetry", async Task<Ok<object>>
    (TelemDb db, string? source, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 100) =>
{
    page = page < 1 ? 1 : page;
    pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

    var q = db.Telemetry.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(source))
        q = q.Where(t => t.Source == source);

    if (startDate.HasValue && endDate.HasValue)
        q = q.Where(t => t.Timestamp >= startDate && t.Timestamp <= endDate);

    var total = await q.CountAsync();
    var rows = await q
        .OrderByDescending(t => t.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new { t.Id, t.Timestamp, t.Source, t.MetricName, t.MetricValue })
        .ToListAsync();

    return TypedResults.Ok(new { items = rows, totalCount = total, page, pageSize });
})
.Produces(StatusCodes.Status200OK);

app.Run();
