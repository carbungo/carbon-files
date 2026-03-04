using System.Net;
using CarbonFiles.Client;
using CarbonFiles.Client.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Resources;

public class FileOperationsTests
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
    public async Task ListAsync_GetsPaginatedFiles()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"items":[{"path":"test.txt","name":"test.txt","size":100,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}],"total":1,"limit":10,"offset":0}""");

        var result = await client.Buckets["b1"].Files.ListAsync(
            new PaginationOptions { Limit = 10, Offset = 0, Sort = "name", Order = "asc" },
            TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(1);
        result.Items[0].Path.Should().Be("test.txt");
        result.Total.Should().Be(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Get);
        var query = req.RequestUri!.Query;
        query.Should().Contain("limit=10");
        query.Should().Contain("offset=0");
        query.Should().Contain("sort=name");
        query.Should().Contain("order=asc");
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files");
    }

    [Fact]
    public async Task ListDirectoryAsync_GetsDirectoryListing_WithPath()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"files":[{"path":"docs/readme.txt","name":"readme.txt","size":50,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}],"folders":["docs/sub"],"total_files":1,"total_folders":1,"limit":20,"offset":0}""");

        var result = await client.Buckets["b1"].Files.ListDirectoryAsync(
            path: "docs",
            pagination: new PaginationOptions { Limit = 20 },
            ct: TestContext.Current.CancellationToken);

        result.Files.Should().HaveCount(1);
        result.Folders.Should().ContainSingle("docs/sub");
        result.TotalFiles.Should().Be(1);
        var query = handler.Requests[0].RequestUri!.Query;
        query.Should().Contain("path=docs");
        query.Should().Contain("limit=20");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/ls");
    }

    [Fact]
    public async Task UploadAsync_SendsPutWithFilename()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"hello.txt","name":"hello.txt","size":13,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        using var stream = new MemoryStream("Hello, World!"u8.ToArray());
        var result = await client.Buckets["b1"].Files.UploadAsync(
            stream, "hello.txt", ct: TestContext.Current.CancellationToken);

        result.Uploaded.Should().HaveCount(1);
        result.Uploaded[0].Name.Should().Be("hello.txt");
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/upload/stream");
        req.RequestUri!.Query.Should().Contain("filename=hello.txt");
    }

    [Fact]
    public async Task UploadAsync_WithProgress_ReportsProgress()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"data.bin","name":"data.bin","size":1024,"mime_type":"application/octet-stream","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        var data = new byte[1024];
        Array.Fill(data, (byte)0x42);
        using var stream = new MemoryStream(data);

        var reports = new List<UploadProgress>();
        var progress = new Progress<UploadProgress>(p => reports.Add(p));

        await client.Buckets["b1"].Files.UploadAsync(
            stream, "data.bin", progress, ct: TestContext.Current.CancellationToken);

        // Progress should have been reported at least once
        reports.Should().NotBeEmpty();
        // The last report should have sent all bytes
        reports[^1].BytesSent.Should().Be(1024);
        reports[^1].TotalBytes.Should().Be(1024);
        reports[^1].Percentage.Should().Be(100);
    }

    [Fact]
    public async Task UploadAsync_WithUploadToken_PassesTokenInQuery()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"file.txt","name":"file.txt","size":5,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        using var stream = new MemoryStream("hello"u8.ToArray());
        await client.Buckets["b1"].Files.UploadAsync(
            stream, "file.txt", uploadToken: "cfu_abc123", ct: TestContext.Current.CancellationToken);

        var query = handler.Requests[0].RequestUri!.Query;
        query.Should().Contain("token=cfu_abc123");
    }

    [Fact]
    public async Task Indexer_GetMetadataAsync_ReturnsFileInfo()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"path":"docs/readme.txt","name":"readme.txt","size":256,"mime_type":"text/plain","short_code":"abc123","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""");

        var file = await client.Buckets["b1"].Files["docs/readme.txt"]
            .GetMetadataAsync(TestContext.Current.CancellationToken);

        file.Path.Should().Be("docs/readme.txt");
        file.Name.Should().Be("readme.txt");
        file.Size.Should().Be(256);
        file.ShortCode.Should().Be("abc123");
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Get);
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files/docs%2Freadme.txt");
    }

    [Fact]
    public async Task Indexer_DownloadAsync_ReturnsStream()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "file-content-here", "application/octet-stream");

        var stream = await client.Buckets["b1"].Files["test.txt"]
            .DownloadAsync(TestContext.Current.CancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        content.Should().Be("file-content-here");
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Get);
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files/test.txt/content");
    }

    [Fact]
    public async Task Indexer_DeleteAsync_DeletesFile()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.Buckets["b1"].Files["test.txt"]
            .DeleteAsync(TestContext.Current.CancellationToken);

        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Delete);
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files/test.txt");
    }

    [Fact]
    public async Task PatchAsync_SendsPatchWithContentRangeHeader()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"path":"test.bin","name":"test.bin","size":1024,"mime_type":"application/octet-stream","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""");

        using var stream = new MemoryStream(new byte[512]);
        var result = await client.Buckets["b1"].Files["test.bin"]
            .PatchAsync(stream, 0, 511, 1024, TestContext.Current.CancellationToken);

        result.Path.Should().Be("test.bin");
        var req = handler.Requests[0];
        req.Method.Method.Should().Be("PATCH");
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files/test.bin/content");
        req.Content!.Headers.GetValues("Content-Range").Should().ContainSingle("bytes 0-511/1024");
    }

    [Fact]
    public async Task AppendAsync_SendsPatchWithXAppendHeader()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"path":"log.txt","name":"log.txt","size":50,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""");

        using var stream = new MemoryStream("appended data"u8.ToArray());
        var result = await client.Buckets["b1"].Files["log.txt"]
            .AppendAsync(stream, TestContext.Current.CancellationToken);

        result.Path.Should().Be("log.txt");
        var req = handler.Requests[0];
        req.Method.Method.Should().Be("PATCH");
        req.RequestUri!.AbsolutePath.Should().Be("/api/buckets/b1/files/log.txt/content");
        req.Headers.GetValues("X-Append").Should().ContainSingle("true");
    }

    [Fact]
    public async Task UploadAsync_ByteArray_SendsContent()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"data.bin","name":"data.bin","size":4,"mime_type":"application/octet-stream","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        var result = await client.Buckets["b1"].Files.UploadAsync(
            new byte[] { 1, 2, 3, 4 }, "data.bin", ct: TestContext.Current.CancellationToken);

        result.Uploaded.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].RequestUri!.Query.Should().Contain("filename=data.bin");
    }

    [Fact]
    public async Task UploadFileAsync_UsesFileNameFromPath()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"test.txt","name":"test.txt","size":5,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        // Create a temp file to upload
        var tempFile = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, "hello", TestContext.Current.CancellationToken);

            var result = await client.Buckets["b1"].Files.UploadFileAsync(
                tempFile, ct: TestContext.Current.CancellationToken);

            result.Uploaded.Should().HaveCount(1);
            handler.Requests[0].RequestUri!.Query.Should().Contain("filename=" + Path.GetFileName(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFileAsync_OverridesFilename()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK,
            """{"uploaded":[{"path":"custom.txt","name":"custom.txt","size":5,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""");

        var tempFile = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, "hello", TestContext.Current.CancellationToken);

            var result = await client.Buckets["b1"].Files.UploadFileAsync(
                tempFile, filename: "custom.txt", ct: TestContext.Current.CancellationToken);

            result.Uploaded.Should().HaveCount(1);
            handler.Requests[0].RequestUri!.Query.Should().Contain("filename=custom.txt");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PatchAsync_ThrowsCarbonFilesException_OnError()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NotFound, """{"error":"File not found","hint":"Check the path"}""");

        using var stream = new MemoryStream(new byte[10]);
        var act = () => client.Buckets["b1"].Files["missing.bin"]
            .PatchAsync(stream, 0, 9, 100, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<CarbonFilesException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.Message.Should().Contain("File not found");
    }
}
