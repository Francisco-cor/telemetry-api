namespace Telemetry.Api.Domain;

public class TelemetryEvent
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double MetricValue { get; set; }
}
