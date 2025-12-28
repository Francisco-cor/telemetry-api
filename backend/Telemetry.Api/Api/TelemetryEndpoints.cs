using Microsoft.AspNetCore.Mvc;
using Telemetry.Api.Services;

namespace Telemetry.Api.Api;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telemetry")
            .WithTags("Telemetry")
            .RequireRateLimiting("fixed-per-ip");

        group.MapPost("/", IngestTelemetry)
            .WithName("PostTelemetry")
            .AddEndpointFilter<ValidationFilter<TelemetryIngestBatch>>()
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithMetadata(new RequestSizeLimitAttribute(256 * 1024));

        group.MapGet("/", QueryTelemetry)
            .WithName("GetTelemetry")
            .Produces<PagedTelemetryResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> IngestTelemetry(
        TelemetryIngestBatch batch,
        ITelemetryService service,
        CancellationToken ct)
    {
        var count = await service.IngestBatchAsync(batch, ct);
        return Results.Created($"/api/telemetry?inserted={count}", new { inserted = count });
    }

    private static async Task<IResult> QueryTelemetry(
        ITelemetryService service,
        [FromQuery] string? source,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await service.QueryTelemetryAsync(source, startDate, endDate, page, pageSize, ct);
        return Results.Ok(result);
    }
}
