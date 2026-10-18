using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class StaticSiteMiddlewareTests : IntegrationTestBase
{
    [Fact]
    public async Task StaticSite_ResolvesRootCleanUrlsAndExactAssets()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        await UploadAsync(admin, bucketId, "index.html", "root index");
        await UploadAsync(admin, bucketId, "about.html", "about page");
        await UploadAsync(admin, bucketId, "team/index.html", "team page");
        await UploadAsync(admin, bucketId, "css/style.css", "body { color: red; }");

        var root = await GetSiteAsync(bucketId, "/");
        (await root.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("root index");
        var about = await GetSiteAsync(bucketId, "/about");
        (await about.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("about page");
        var team = await GetSiteAsync(bucketId, "/team");
        (await team.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("team page");

        var asset = await GetSiteAsync(bucketId, "/css/style.css");
        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await asset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("body { color: red; }");
        asset.Content.Headers.ContentType!.MediaType.Should().Be("text/css");
    }

    [Fact]
    public async Task StaticSite_SpaModeFallsBackToRootIndex()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, spaMode: true);
        await UploadAsync(admin, bucketId, "index.html", "spa shell");

        var response = await GetSiteAsync(bucketId, "/client/route");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("spa shell");
    }

    [Fact]
    public async Task StaticSite_NonSpaModeServesCustom404WithNotFoundStatus()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        await UploadAsync(admin, bucketId, "404.html", "custom missing page");

        var response = await GetSiteAsync(bucketId, "/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("custom missing page");
    }

    [Fact]
    public async Task StaticSite_ReusesRangeRequestHandling()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        await UploadAsync(admin, bucketId, "video.txt", "0123456789");
        using var request = SiteRequest(bucketId, "/video.txt");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("2345");
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 2-5/10");
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
    }

    [Fact]
    public async Task StaticSite_ServesCorrectTypesAndCachePolicies()
    {
        using var admin = Fixture.CreateAdminClient();
        var id = await CreateBucketAsync(admin);
        await UploadAsync(admin, id, "index.html", "<html>home</html>");
        await UploadAsync(admin, id, "site.webmanifest", "{\"name\":\"test\"}");
        await UploadAsync(admin, id, "data.json", "{\"ok\":true}");
        await UploadAsync(admin, id, "LICENSE", "plain text");
        await UploadAsync(admin, id, "_next/static/app-123.js", "console.log('ok')");
        await Fixture.SetStoredMimeTypeAsync(id, "site.webmanifest", "application/octet-stream");

        var manifest = await GetSiteAsync(id, "/site.webmanifest");
        manifest.Content.Headers.ContentType!.ToString().Should().Be("application/manifest+json; charset=utf-8");
        using var apiManifest = await Fixture.Client.GetAsync($"/api/buckets/{id}/files/site.webmanifest/content", TestContext.Current.CancellationToken);
        apiManifest.Content.Headers.ContentType!.ToString().Should().Be("application/manifest+json; charset=utf-8");
        var json = await GetSiteAsync(id, "/data.json");
        json.Content.Headers.ContentType!.ToString().Should().Be("application/json; charset=utf-8");
        var license = await GetSiteAsync(id, "/LICENSE");
        license.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8");
        using var metadata = await Fixture.Client.GetAsync($"/api/buckets/{id}/files/LICENSE", TestContext.Current.CancellationToken);
        using var metadataJson = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        metadataJson.RootElement.GetProperty("mime_type").GetString().Should().Be("text/plain");
        var html = await GetSiteAsync(id, "/");
        html.Headers.CacheControl!.ToString().Should().Be("public, no-cache");
        html.Headers.ETag.Should().NotBeNull();
        html.Content.Headers.LastModified.Should().NotBeNull();
        html.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        var asset = await GetSiteAsync(id, "/_next/static/app-123.js");
        asset.Headers.CacheControl!.ToString().Should().Be("public, max-age=31536000, immutable");

        using var conditional = SiteRequest(id, "/");
        conditional.Headers.IfNoneMatch.Add(html.Headers.ETag!);
        var notModified = await Fixture.Client.SendAsync(conditional, TestContext.Current.CancellationToken);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        notModified.Headers.ETag.Should().Be(html.Headers.ETag);
        notModified.Headers.CacheControl!.ToString().Should().Be("public, no-cache");

        using var modifiedSince = SiteRequest(id, "/");
        modifiedSince.Headers.IfModifiedSince = html.Content.Headers.LastModified;
        var sinceResponse = await Fixture.Client.SendAsync(modifiedSince, TestContext.Current.CancellationToken);
        sinceResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);

        using var head = new HttpRequestMessage(HttpMethod.Head, "/");
        head.Headers.Host = $"{id}.files.test";
        var headResponse = await Fixture.Client.SendAsync(head, TestContext.Current.CancellationToken);
        headResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        headResponse.Headers.ETag.Should().Be(html.Headers.ETag);
        (await headResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task StaticSite_CompressionRequest_CompressesTextAndSetsVary(string encoding)
    {
        using var admin = Fixture.CreateAdminClient();
        var id = await CreateBucketAsync(admin);
        var body = new string('x', 10000);
        await UploadAsync(admin, id, "index.html", body);
        using var request = SiteRequest(id, "/");
        request.Headers.AcceptEncoding.ParseAdd(encoding);
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Contain(encoding);
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        await using var compressed = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await using Stream uncompressed = encoding == "gzip"
            ? new GZipStream(compressed, CompressionMode.Decompress)
            : new BrotliStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(uncompressed);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be(body);
    }

    [Fact]
    public async Task StaticSite_ExtensionlessBinary_SniffsMagicAndUnknownBytes()
    {
        using var admin = Fixture.CreateAdminClient();
        var id = await CreateBucketAsync(admin);
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00 };
        using (var body = new ByteArrayContent(png))
        {
            var upload = await admin.PutAsync($"/api/buckets/{id}/upload/stream?filename=image", body,
                TestContext.Current.CancellationToken);
            upload.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        using (var body = new ByteArrayContent([0x00, 0xff, 0x10]))
        {
            var upload = await admin.PutAsync($"/api/buckets/{id}/upload/stream?filename=blob", body,
                TestContext.Current.CancellationToken);
            upload.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var image = await GetSiteAsync(id, "/image");
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var blob = await GetSiteAsync(id, "/blob");
        blob.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
    }

    private async Task<string> CreateBucketAsync(HttpClient client, bool spaMode = false)
    {
        var response = await client.PostAsJsonAsync("/api/buckets",
            new { name = $"site-{Guid.NewGuid():N}", spa_mode = spaMode }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task UploadAsync(HttpClient client, string bucketId, string path, string content)
    {
        using var body = new StringContent(content, Encoding.UTF8);
        var response = await client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename={Uri.EscapeDataString(path)}", body,
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> GetSiteAsync(string bucketId, string path)
    {
        using var request = SiteRequest(bucketId, path);
        return await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage SiteRequest(string bucketId, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = $"{bucketId.ToLowerInvariant()}.files.test";
        return request;
    }
}
