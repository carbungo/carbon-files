using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("2345");
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 2-5/10");
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
