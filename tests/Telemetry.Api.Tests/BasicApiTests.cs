// tests/Telemetry.Api.Tests/BasicApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class BasicApiTests : IClassFixture<TelemetryApiFactory>
{
    private readonly HttpClient _client;

    public BasicApiTests(TelemetryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostTelemetry_Should_Return201_And_Insert2()
    {
        var payload = new
        {
            events = new[]
            {
                new { timestamp = DateTime.UtcNow, source = "T-001", metricName = "RPM", metricValue = 1500.0 },
                new { timestamp = DateTime.UtcNow.AddMinutes(1), source = "T-001", metricName = "Fuel", metricValue = 2.3 }
            }
        };

        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("inserted").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task PostTelemetry_InvalidPayload_Should_Return400()
    {
        var payload = new { events = Array.Empty<object>() }; // Payload no válido (lista vacía)
        var res = await _client.PostAsJsonAsync("/api/telemetry", payload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTelemetry_BySource_Should_Return200_And_Items()
    {
        // Paso 1: Insertar datos de prueba (seed)
        var seedPayload = new
        {
            events = new[] { new { timestamp = DateTime.UtcNow, source = "T-002", metricName = "Temp", metricValue = 70.5 } }
        };
        await _client.PostAsJsonAsync("/api/telemetry", seedPayload);

        // Paso 2: Realizar la consulta
        var res = await _client.GetAsync("/api/telemetry?source=T-002&page=1&pageSize=50");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        var items = doc.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        items[0].GetProperty("source").GetString().Should().Be("T-002");
    }
}