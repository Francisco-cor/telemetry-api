using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class HealthCheckTests : IClassFixture<TelemetryApiFactory>
{
    private readonly HttpClient _client;

    public HealthCheckTests(TelemetryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthLive_Should_Return200_With_HealthyStatus()
    {
        var res = await _client.GetAsync("/health/live");

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealthReady_Should_Return200_When_Db_Is_Reachable()
    {
        var res = await _client.GetAsync("/health/ready");

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Healthy");
        doc.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0);
    }
}
