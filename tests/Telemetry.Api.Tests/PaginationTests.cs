using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class PaginationTests : IClassFixture<TelemetryApiFactory>
{
    private readonly HttpClient _client;

    public PaginationTests(TelemetryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task SeedEventsAsync(string source, int count)
    {
        var events = Enumerable.Range(0, count).Select(i => new
        {
            timestamp = DateTime.UtcNow.AddMinutes(-i),
            source,
            metricName = "RPM",
            metricValue = (double)(1000 + i)
        }).ToArray();

        var res = await _client.PostAsJsonAsync("/api/telemetry", new { events });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetTelemetry_FirstPage_Should_HaveHasNextTrue_When_MoreItemsExist()
    {
        var source = "PAG-001";
        await SeedEventsAsync(source, 6);

        var res = await _client.GetAsync($"/api/telemetry?source={source}&pageSize=3");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("items").GetArrayLength().Should().Be(3);
        doc.GetProperty("hasNext").GetBoolean().Should().BeTrue();
        doc.GetProperty("nextTimestamp").ValueKind.Should().NotBe(JsonValueKind.Null);
        doc.GetProperty("nextId").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetTelemetry_SecondPage_Should_HaveHasNextFalse_When_NoMoreItems()
    {
        var source = "PAG-002";
        await SeedEventsAsync(source, 4);

        // Page 1
        var page1 = await _client.GetAsync($"/api/telemetry?source={source}&pageSize=3");
        var doc1 = await page1.Content.ReadFromJsonAsync<JsonElement>();
        doc1.GetProperty("hasNext").GetBoolean().Should().BeTrue();

        var nextTs = doc1.GetProperty("nextTimestamp").GetString();
        var nextId = doc1.GetProperty("nextId").GetString();

        // Page 2 using cursor
        var page2 = await _client.GetAsync(
            $"/api/telemetry?source={source}&pageSize=3&afterTimestamp={Uri.EscapeDataString(nextTs!)}&afterId={nextId}");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc2 = await page2.Content.ReadFromJsonAsync<JsonElement>();
        doc2.GetProperty("items").GetArrayLength().Should().Be(1);
        doc2.GetProperty("hasNext").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetTelemetry_PageSizeOutOfRange_Should_DefaultTo100()
    {
        var res = await _client.GetAsync("/api/telemetry?pageSize=9999");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("pageSize").GetInt32().Should().Be(100);
    }
}
