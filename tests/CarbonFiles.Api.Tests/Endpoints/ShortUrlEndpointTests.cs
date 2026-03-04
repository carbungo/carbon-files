using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class ShortUrlEndpointTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient client, string? expiresIn = null)
    {
        var payload = expiresIn != null
            ? new { name = $"short-url-test-{Guid.NewGuid():N}", expires_in = expiresIn }
            : (object)new { name = $"short-url-test-{Guid.NewGuid():N}" };

        var response = await client.PostAsJsonAsync("/api/buckets", payload, TestContext.Current.CancellationToken);
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

    // ── Resolve Short URL ───────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ValidCode_Returns302RedirectToContent()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileInfo = await UploadFileAsync(client, bucketId, "hello.txt", "Hello, World!");
        var shortCode = fileInfo.GetProperty("short_code").GetString()!;

        using var noRedirectClient = Fixture.CreateNoRedirectClient();
        var response = await noRedirectClient.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/api/buckets/{bucketId}/files/hello.txt/content");
    }

    [Fact]
    public async Task Resolve_NonexistentCode_Returns404()
    {
        var response = await Fixture.Client.GetAsync("/s/zzzzzz", TestContext.Current.CancellationToken);

        // The default client follows redirects, so a 404 means no redirect was found
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_ExpiredBucket_Returns404()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileInfo = await UploadFileAsync(client, bucketId, "expire-test.txt", "will expire");
        var shortCode = fileInfo.GetProperty("short_code").GetString()!;

        // Set the bucket expiry to a Unix epoch in the past (1 hour ago)
        var pastEpoch = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString();
        var updateResp = await client.PatchAsJsonAsync($"/api/buckets/{bucketId}", new { expires_in = pastEpoch }, TestContext.Current.CancellationToken);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var noRedirectClient = Fixture.CreateNoRedirectClient();
        var response = await noRedirectClient.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Delete Short URL ────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AsAdmin_Returns204()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileInfo = await UploadFileAsync(client, bucketId, "del-short.txt", "delete short url");
        var shortCode = fileInfo.GetProperty("short_code").GetString()!;

        var response = await client.DeleteAsync($"/api/short/{shortCode}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AsPublic_Returns401()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileInfo = await UploadFileAsync(client, bucketId, "pub-short.txt", "public delete test");
        var shortCode = fileInfo.GetProperty("short_code").GetString()!;

        // Use the unauthenticated client
        var response = await Fixture.Client.DeleteAsync($"/api/short/{shortCode}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_ShortUrlOnly_FileStillExists()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileInfo = await UploadFileAsync(client, bucketId, "keep-file.txt", "file stays");
        var shortCode = fileInfo.GetProperty("short_code").GetString()!;

        // Delete the short URL
        var deleteResp = await client.DeleteAsync($"/api/short/{shortCode}", TestContext.Current.CancellationToken);
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The short URL should no longer resolve
        using var noRedirectClient = Fixture.CreateNoRedirectClient();
        var resolveResp = await noRedirectClient.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);
        resolveResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // But the file itself should still exist
        var fileResp = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/keep-file.txt", TestContext.Current.CancellationToken);
        fileResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // And the file content should still be downloadable
        var contentResp = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/keep-file.txt/content", TestContext.Current.CancellationToken);
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await contentResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be("file stays");
    }

    [Fact]
    public async Task Delete_NonexistentCode_Returns404()
    {
        using var client = Fixture.CreateAdminClient();

        var response = await client.DeleteAsync("/api/short/zzzzzz", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
