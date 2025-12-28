using Telemetry.Api.Api;

namespace Telemetry.Api.Services;

public interface ITelemetryService
{
    Task<int> IngestBatchAsync(TelemetryIngestBatch batch, CancellationToken ct = default);
    Task<PagedTelemetryResponse> QueryTelemetryAsync(string? source, DateTime? startDate, DateTime? endDate, int page, int pageSize, CancellationToken ct = default);
}
