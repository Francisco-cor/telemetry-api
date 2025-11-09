namespace Telemetry.Api.Api;

public record TelemetryIngestDto(
    DateTime Timestamp,
    string Source,
    string MetricName,
    double MetricValue);

public class TelemetryIngestBatch
{
    public List<TelemetryIngestDto> Events { get; set; } = new();
}
