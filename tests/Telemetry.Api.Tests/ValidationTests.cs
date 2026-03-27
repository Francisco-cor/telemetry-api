using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

public class ValidationTests : IClassFixture<TelemetryApiFactory>
{
    private readonly HttpClient _client;

    public ValidationTests(TelemetryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostTelemetry_TimestampTooOld_Should_Return400()
    {
        var payload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow.AddDays(-8), source = "V-001", metricName = "RPM", metricValue = 1000.0 } }
        };
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTelemetry_TimestampTooFarInFuture_Should_Return400()
    {
        var payload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow.AddHours(2), source = "V-001", metricName = "RPM", metricValue = 1000.0 } }
        };
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTelemetry_InvalidSourceChars_Should_Return400()
    {
        var payload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow, source = "invalid source!", metricName = "RPM", metricValue = 1000.0 } }
        };
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTelemetry_InvalidMetricNameChars_Should_Return400()
    {
        var payload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow, source = "V-001", metricName = "metric name@bad", metricValue = 1000.0 } }
        };
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTelemetry_SourceTooLong_Should_Return400()
    {
        var payload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow, source = new string('A', 101), metricName = "RPM", metricValue = 1000.0 } }
        };
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
