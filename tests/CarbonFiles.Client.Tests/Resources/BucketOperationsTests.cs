using System.Net;
using CarbonFiles.Client;
using CarbonFiles.Client.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Resources;

public class BucketOperationsTests
{
    private static (CarbonFilesClient Client, MockHandler Handler) CreateClient()
    {
        var handler = new MockHandler();
        var client = new CarbonFilesClient(new CarbonFilesClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
            ApiKey = "test-key",
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") }
        });
        return (client, handler);
    }

    [Fact]
    public async Task CreateAsync_PostsToBuckets_ReturnsBucket()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Created,
            """{"id":"b1","name":"my-bucket","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}""");

        var bucket = await client.Buckets.CreateAsync(
            new CreateBucketRequest { Name = "my-bucket" },
            TestContext.Current.CancellationToken);

        bucket.Id.Should().Be("b1");
        bucket.Name.Should().Be("my-bucket");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets");
    }

    [Fact]
    public async Task ListAsync_GetsPaginatedResults_WithQueryParams()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"items":[{"id":"b1","name":"test","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}],"total":1,"limit":10,"offset":0}""");

        var result = await client.Buckets.ListAsync(
            pagination: new PaginationOptions { Limit = 10, Offset = 0, Sort = "name", Order = "asc" },
            includeExpired: true,
            ct: TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(1);
        result.Total.Should().Be(1);
        var query = handler.Requests[0].RequestUri!.Query;
        query.Should().Contain("limit=10");
        query.Should().Contain("offset=0");
        query.Should().Contain("sort=name");
        query.Should().Contain("order=asc");
        query.Should().Contain("include_expired=true");
    }

    [Fact]
    public async Task ListAsync_NoParams_OmitsQueryString()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"items":[],"total":0,"limit":20,"offset":0}""");

        await client.Buckets.ListAsync(ct: TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task Indexer_GetAsync_FetchesBucketDetail()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"b1","name":"test","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":3,"total_size":1024,"files":[],"has_more_files":false}""");

        var detail = await client.Buckets["b1"].GetAsync(TestContext.Current.CancellationToken);

        detail.Id.Should().Be("b1");
        detail.FileCount.Should().Be(3);
        detail.TotalSize.Should().Be(1024);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1");
    }

    [Fact]
    public async Task Indexer_UpdateAsync_PatchesBucket()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"b1","name":"renamed","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}""");

        var bucket = await client.Buckets["b1"].UpdateAsync(
            new UpdateBucketRequest { Name = "renamed" },
            TestContext.Current.CancellationToken);

        bucket.Name.Should().Be("renamed");
        handler.Requests[0].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1");
    }

    [Fact]
    public async Task Indexer_DeleteAsync_DeletesBucket()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.Buckets["b1"].DeleteAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1");
    }

    [Fact]
    public async Task Indexer_GetSummaryAsync_ReturnsPlainText()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "Bucket has 3 files totalling 1024 bytes", "text/plain");

        var summary = await client.Buckets["b1"].GetSummaryAsync(TestContext.Current.CancellationToken);

        summary.Should().Be("Bucket has 3 files totalling 1024 bytes");
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1/summary");
    }

    [Fact]
    public async Task Indexer_DownloadZipAsync_ReturnsStream()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "PK-fake-zip-content", "application/zip");

        var stream = await client.Buckets["b1"].DownloadZipAsync(TestContext.Current.CancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        content.Should().Be("PK-fake-zip-content");
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1/zip");
    }

    [Fact]
    public void Indexer_Files_IsAccessible()
    {
        var (client, _) = CreateClient();

        client.Buckets["b1"].Files.Should().NotBeNull();
    }

    [Fact]
    public void Indexer_Tokens_IsAccessible()
    {
        var (client, _) = CreateClient();

        client.Buckets["b1"].Tokens.Should().NotBeNull();
    }

    [Fact]
    public async Task Indexer_EscapesBucketId()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"a/b","name":"test","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0,"files":[],"has_more_files":false}""");

        await client.Buckets["a/b"].GetAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/a%2Fb");
    }
}
