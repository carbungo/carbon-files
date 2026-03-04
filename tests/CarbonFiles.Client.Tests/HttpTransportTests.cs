using System.Net;
using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests;

public class HttpTransportTests
{
    private static (HttpTransport Transport, MockHandler Handler) Create(string apiKey = "test-key")
    {
        var handler = new MockHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var transport = new HttpTransport(http, apiKey);
        return (transport, handler);
    }

    [Fact]
    public async Task GetAsync_SendsAuthHeader()
    {
        var (transport, handler) = Create("my-api-key");
        handler.Enqueue(HttpStatusCode.OK, """{"status":"ok","uptime_seconds":100,"db":"ok"}""");

        await transport.GetAsync<HealthResponse>("/healthz", TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("my-api-key");
    }

    [Fact]
    public async Task GetAsync_DeserializesResponse()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"status":"ok","uptime_seconds":100,"db":"ok"}""");

        var result = await transport.GetAsync<HealthResponse>("/healthz", TestContext.Current.CancellationToken);

        result.Status.Should().Be("ok");
        result.UptimeSeconds.Should().Be(100);
    }

    [Fact]
    public async Task GetAsync_ThrowsCarbonFilesException_On4xx()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.NotFound, """{"error":"not found","hint":"check id"}""");

        var act = () => transport.GetAsync<Bucket>("/api/buckets/nope", TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<CarbonFilesException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.Error.Should().Be("not found");
        ex.Which.Hint.Should().Be("check id");
    }

    [Fact]
    public async Task PostAsync_SendsJsonBody()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created, """{"id":"abc","name":"test","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}""");

        await transport.PostAsync<CreateBucketRequest, Bucket>(
            "/api/buckets",
            new CreateBucketRequest { Name = "test" },
            TestContext.Current.CancellationToken);

        handler.RequestBodies[0].Should().Contain("\"name\"").And.Contain("\"test\"");
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await transport.DeleteAsync("/api/buckets/abc", TestContext.Current.CancellationToken);

        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/abc");
    }

    [Fact]
    public async Task PatchAsync_SendsPatchRequest()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"abc","name":"updated","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}""");

        var result = await transport.PatchAsync<UpdateBucketRequest, Bucket>(
            "/api/buckets/abc",
            new UpdateBucketRequest { Name = "updated" },
            TestContext.Current.CancellationToken);

        handler.Requests[0].Method.Should().Be(HttpMethod.Patch);
        result.Name.Should().Be("updated");
    }

    [Fact]
    public async Task GetStringAsync_ReturnsPlainText()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "Bucket summary text", "text/plain");

        var result = await transport.GetStringAsync("/api/buckets/abc/summary", TestContext.Current.CancellationToken);

        result.Should().Be("Bucket summary text");
    }

    [Fact]
    public async Task GetStreamAsync_ReturnsStream()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "binary-content", "application/zip");

        var stream = await transport.GetStreamAsync("/api/buckets/abc/zip", TestContext.Current.CancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        content.Should().Be("binary-content");
    }

    [Fact]
    public async Task GetAsync_BuildsQueryString()
    {
        var (transport, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"items":[],"total":0,"limit":10,"offset":5}""");

        await transport.GetAsync<PaginatedResponse<Bucket>>(
            "/api/buckets",
            new Dictionary<string, string?> { ["limit"] = "10", ["offset"] = "5" },
            TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.Query.Should().Contain("limit=10").And.Contain("offset=5");
    }

    [Fact]
    public async Task CancellationToken_IsPropagated()
    {
        var (transport, handler) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => transport.GetAsync<HealthResponse>("/healthz", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
