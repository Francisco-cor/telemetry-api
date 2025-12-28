using Microsoft.EntityFrameworkCore;
using Telemetry.Api.Api;
using Telemetry.Api.Domain;
using Telemetry.Api.Infra;

namespace Telemetry.Api.Services;

public class TelemetryService : ITelemetryService
{
    private readonly TelemDb _db;

    public TelemetryService(TelemDb db)
    {
        _db = db;
    }

    public async Task<int> IngestBatchAsync(TelemetryIngestBatch batch, CancellationToken ct = default)
    {
        // La validación ya ocurrió en el filtro/pipeline
        var events = batch.Events.Select(e => new TelemetryEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = e.Timestamp,
            Source = e.Source,
            MetricName = e.MetricName,
            MetricValue = e.MetricValue
        }).ToList();

        await _db.Telemetry.AddRangeAsync(events, ct);
        await _db.SaveChangesAsync(ct);
        return events.Count;
    }

    public async Task<PagedTelemetryResponse> QueryTelemetryAsync(string? source, DateTime? startDate, DateTime? endDate, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

        var query = _db.Telemetry.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(t => t.Source == source);

        if (startDate.HasValue && endDate.HasValue)
            query = query.Where(t => t.Timestamp >= startDate.Value && t.Timestamp <= endDate.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TelemetryDto(t.Id, t.Timestamp, t.Source, t.MetricName, t.MetricValue))
            .ToListAsync(ct);

        return new PagedTelemetryResponse(items, totalCount, page, pageSize);
    }
}
