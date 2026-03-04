using System.Net;
using CarbonFiles.Client;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Resources;

public class HealthOperationsTests
{
    [Fact]
    public async Task CheckAsync_ReturnsHealthResponse()
    {
        var handler = new MockHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"status":"ok","uptime_seconds":3600,"db":"ok"}""");
        var client = new CarbonFilesClient(new CarbonFilesClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") }
        });

        var health = await client.Health.CheckAsync(TestContext.Current.CancellationToken);

        health.Status.Should().Be("ok");
        health.UptimeSeconds.Should().Be(3600);
        health.Db.Should().Be("ok");
    }

    [Fact]
    public async Task CheckAsync_ThrowsOnError()
    {
        var handler = new MockHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":"unhealthy","hint":"db down"}""");
        var client = new CarbonFilesClient(new CarbonFilesClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") }
        });

        var act = () => client.Health.CheckAsync(TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<CarbonFilesException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void Client_StringConstructor_SetsProperties()
    {
        var client = new CarbonFilesClient("https://example.com", "my-key");
        client.Health.Should().NotBeNull();
        client.Buckets.Should().NotBeNull();
        client.Keys.Should().NotBeNull();
        client.Stats.Should().NotBeNull();
        client.ShortUrls.Should().NotBeNull();
        client.Dashboard.Should().NotBeNull();
    }
}
