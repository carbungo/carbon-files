using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class FileEndpointTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient? client = null)
    {
        var c = client ?? Fixture.CreateAdminClient();
        var response = await c.PostAsJsonAsync("/api/buckets", new { name = $"file-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> UploadFileAsync(HttpClient client, string bucketId, string fileName, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);

        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("uploaded")[0].Clone();
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── List Files ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListFiles_EmptyBucket_ReturnsEmptyList()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("items").GetArrayLength().Should().Be(0);
        body.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListFiles_WithFiles_ReturnsPaginated()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload several files
        await UploadFileAsync(client, bucketId, "file1.txt", "content1");
        await UploadFileAsync(client, bucketId, "file2.txt", "content2");
        await UploadFileAsync(client, bucketId, "file3.txt", "content3");

        // List with pagination
        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files?limit=2&offset=0", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("items").GetArrayLength().Should().Be(2);
        body.GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("limit").GetInt32().Should().Be(2);
        body.GetProperty("offset").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListFiles_NonexistentBucket_Returns404()
    {
        var response = await Fixture.Client.GetAsync("/api/buckets/nonexistent/files", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Get File Metadata ───────────────────────────────────────────────

    [Fact]
    public async Task GetFileMetadata_ReturnsCorrectInfo()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "readme.md", "# Hello");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/readme.md", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("path").GetString().Should().Be("readme.md");
        body.GetProperty("name").GetString().Should().Be("readme.md");
        body.GetProperty("size").GetInt64().Should().Be(7); // "# Hello" = 7 bytes
        body.GetProperty("mime_type").GetString().Should().Be("text/markdown");
        body.TryGetProperty("short_code", out var sc).Should().BeTrue();
        sc.GetString().Should().HaveLength(6);
        body.TryGetProperty("short_url", out var su).Should().BeTrue();
        su.GetString().Should().StartWith("/s/");
    }

    [Fact]
    public async Task GetFileMetadata_NonexistentFile_Returns404()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/nonexistent.txt", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Download File Content ───────────────────────────────────────────

    [Fact]
    public async Task DownloadFile_ReturnsContentWithCorrectHeaders()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileContent = "Hello, download world!";
        await UploadFileAsync(client, bucketId, "download.txt", fileContent);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/download.txt/content", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check content
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be(fileContent);

        // Check headers
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.TryGetValues("Cache-Control", out var cacheControl).Should().BeTrue();
        response.Headers.TryGetValues("Accept-Ranges", out var acceptRanges).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFile_ETagConditionalRequest_Returns304()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "etag-test.txt", "etag content");

        // First request to get ETag
        var response1 = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/etag-test.txt/content", TestContext.Current.CancellationToken);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = response1.Headers.ETag!.Tag;

        // Second request with If-None-Match
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/buckets/{bucketId}/files/etag-test.txt/content");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));

        var response2 = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response2.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task DownloadFile_IfModifiedSince_Returns304()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "modified-test.txt", "modified content");

        // Use a future date for If-Modified-Since
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/buckets/{bucketId}/files/modified-test.txt/content");
        request.Headers.IfModifiedSince = DateTimeOffset.UtcNow.AddHours(1);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task DownloadFile_DownloadTrue_AddsContentDisposition()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "attachment.txt", "download me");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/attachment.txt/content?download=true", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Be("attachment.txt");
    }

    [Fact]
    public async Task DownloadFile_NonexistentFile_Returns404()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/nonexistent.txt/content", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Delete File ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteFile_AsAdmin_Returns204()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "to-delete.txt", "delete me");

        var response = await client.DeleteAsync($"/api/buckets/{bucketId}/files/to-delete.txt", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/to-delete.txt", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFile_AsOwner_Returns204()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create an API key and bucket
        var keyResp = await admin.PostAsJsonAsync("/api/keys", new { name = "file-deleter" }, TestContext.Current.CancellationToken);
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = await ParseJsonAsync(keyResp);
        var apiKey = keyBody.GetProperty("key").GetString()!;

        using var ownerClient = Fixture.CreateAuthenticatedClient(apiKey);

        var bucketResp = await ownerClient.PostAsJsonAsync("/api/buckets", new { name = "delete-test" }, TestContext.Current.CancellationToken);
        var bucketBody = await ParseJsonAsync(bucketResp);
        var bucketId = bucketBody.GetProperty("id").GetString()!;

        await UploadFileAsync(ownerClient, bucketId, "owner-delete.txt", "owner file");

        var response = await ownerClient.DeleteAsync($"/api/buckets/{bucketId}/files/owner-delete.txt", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteFile_AsPublic_Returns403()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "no-delete.txt", "protected");

        var response = await Fixture.Client.DeleteAsync($"/api/buckets/{bucketId}/files/no-delete.txt", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteFile_Nonexistent_Returns404()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.DeleteAsync($"/api/buckets/{bucketId}/files/nonexistent.txt", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Bucket Stats Updated After Upload ───────────────────────────────

    [Fact]
    public async Task Upload_UpdatesBucketStats()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "stat1.txt", "12345");
        await UploadFileAsync(client, bucketId, "stat2.txt", "67890");

        // Check bucket detail shows correct stats
        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("file_count").GetInt32().Should().Be(2);
        body.GetProperty("total_size").GetInt64().Should().Be(10); // 5 + 5 bytes
    }

    [Fact]
    public async Task Delete_UpdatesBucketStats()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "del-stat.txt", "12345");

        // Verify file count is 1
        var response1 = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        var body1 = await ParseJsonAsync(response1);
        body1.GetProperty("file_count").GetInt32().Should().Be(1);

        // Delete the file
        await client.DeleteAsync($"/api/buckets/{bucketId}/files/del-stat.txt", TestContext.Current.CancellationToken);

        // Verify file count is 0
        var response2 = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        var body2 = await ParseJsonAsync(response2);
        body2.GetProperty("file_count").GetInt32().Should().Be(0);
        body2.GetProperty("total_size").GetInt64().Should().Be(0);
    }

    // ── MIME Type Detection ─────────────────────────────────────────────

    [Fact]
    public async Task Upload_DetectsMimeType()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var files = new Dictionary<string, string>
        {
            ["image.png"] = "image/png",
            ["data.json"] = "application/json",
            ["style.css"] = "text/css",
        };

        foreach (var (fileName, expectedMime) in files)
        {
            var fileInfo = await UploadFileAsync(client, bucketId, fileName, "dummy content");
            fileInfo.GetProperty("mime_type").GetString().Should().Be(expectedMime, $"because {fileName} should be {expectedMime}");
        }
    }

    // ── Tree Mode (/files?delimiter=/) ──────────────────────────────────

    [Fact]
    public async Task ListFiles_WithDelimiter_ReturnsTreeStructure()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "readme.md", "root file");
        await UploadFileAsync(client, bucketId, "src/main.cs", "main");
        await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
        await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");
        await UploadFileAsync(client, bucketId, "docs/guide.md", "guide");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);

        json.GetProperty("files").GetArrayLength().Should().Be(1);
        json.GetProperty("files")[0].GetProperty("path").GetString().Should().Be("readme.md");
        json.GetProperty("directories").GetArrayLength().Should().Be(2);
        json.GetProperty("delimiter").GetString().Should().Be("/");
    }

    [Fact]
    public async Task ListFiles_WithDelimiterAndPrefix_ReturnsScoped()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "src/main.cs", "main");
        await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
        await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/&prefix=src/",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);

        json.GetProperty("prefix").GetString().Should().Be("src/");
        json.GetProperty("files").GetArrayLength().Should().Be(1);
        json.GetProperty("directories").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ListFiles_WithDelimiterDeepPrefix_ReturnsLeafFiles()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
        await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/&prefix=src/utils/",
            TestContext.Current.CancellationToken);
        var json = await ParseJsonAsync(response);

        json.GetProperty("files").GetArrayLength().Should().Be(2);
        json.GetProperty("directories").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListFiles_NoDelimiter_ReturnsFlatList()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "src/main.cs", "main");
        await UploadFileAsync(client, bucketId, "docs/guide.md", "guide");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files",
            TestContext.Current.CancellationToken);
        var json = await ParseJsonAsync(response);

        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ListFiles_TreeMode_DirectoriesHaveStats()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "src/a.txt", "aaa");
        await UploadFileAsync(client, bucketId, "src/b.txt", "bbb");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/",
            TestContext.Current.CancellationToken);
        var json = await ParseJsonAsync(response);

        var dir = json.GetProperty("directories")[0];
        dir.GetProperty("path").GetString().Should().Be("src/");
        dir.GetProperty("file_count").GetInt32().Should().Be(2);
        dir.GetProperty("total_size").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListFiles_TreeModeCursorPagination()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        for (int i = 0; i < 5; i++)
            await UploadFileAsync(client, bucketId, $"file{i:D2}.txt", $"content{i}");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/&limit=2",
            TestContext.Current.CancellationToken);
        var json = await ParseJsonAsync(response);

        json.GetProperty("files").GetArrayLength().Should().Be(2);
        var cursor = json.GetProperty("cursor").GetString();
        cursor.Should().NotBeNull();

        var response2 = await client.GetAsync(
            $"/api/buckets/{bucketId}/files?delimiter=/&limit=2&cursor={cursor}",
            TestContext.Current.CancellationToken);
        var json2 = await ParseJsonAsync(response2);

        json2.GetProperty("files").GetArrayLength().Should().Be(2);
    }

    // ── Verify Endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyFile_ValidContent_ReturnsValid()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        await UploadFileAsync(client, bucketId, "test.txt", "verify me");

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files/test.txt/verify",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);

        json.GetProperty("path").GetString().Should().Be("test.txt");
        json.GetProperty("valid").GetBoolean().Should().BeTrue();
        json.GetProperty("stored_hash").GetString()
            .Should().Be(json.GetProperty("computed_hash").GetString());
    }

    [Fact]
    public async Task VerifyFile_NonexistentFile_Returns404()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.GetAsync(
            $"/api/buckets/{bucketId}/files/nope.txt/verify",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
