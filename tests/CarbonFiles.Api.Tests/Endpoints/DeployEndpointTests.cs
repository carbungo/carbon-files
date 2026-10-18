using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class DeployEndpointTests : IntegrationTestBase
{
    [Fact]
    public async Task SyncDeploy_VerifiesManifestBeforePruningAndAcceptsScopedToken()
    {
        using var admin = Fixture.CreateAdminClient();
        var id = await CreateBucketAsync(admin);
        var other = await CreateBucketAsync(admin);
        var indexHash = await UploadAsync(admin, id, "index.html", "new home");
        await UploadAsync(admin, id, "stale.txt", "old file");
        var token = await CreateTokenAsync(admin, id);

        using var foreign = await Fixture.Client.PostAsJsonAsync($"/api/buckets/{other}/deploy/sync?token={token}",
            new { files = new[] { new { path = "index.html", sha256 = indexHash } } }, TestContext.Current.CancellationToken);
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var mismatch = await Fixture.Client.PostAsJsonAsync($"/api/buckets/{id}/deploy/sync?token={token}",
            new { files = new[] { new { path = "index.html", sha256 = new string('0', 64) } } }, TestContext.Current.CancellationToken);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Fixture.Client.GetAsync($"/api/buckets/{id}/files/stale.txt", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var synced = await Fixture.Client.PostAsJsonAsync($"/api/buckets/{id}/deploy/sync?token={token}",
            new { files = new[] { new { path = "index.html", sha256 = indexHash } } }, TestContext.Current.CancellationToken);
        synced.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await synced.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("kept").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("deleted").GetInt32().Should().Be(1);
        (await Fixture.Client.GetAsync($"/api/buckets/{id}/files/stale.txt", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var detail = await Fixture.Client.GetAsync($"/api/buckets/{id}", TestContext.Current.CancellationToken);
        using var bucket = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        bucket.RootElement.GetProperty("file_count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SyncDeploy_RejectsEmptyDuplicateAndMissingFiles()
    {
        using var admin = Fixture.CreateAdminClient();
        var id = await CreateBucketAsync(admin);
        var hash = await UploadAsync(admin, id, "index.html", "home");
        var endpoint = $"/api/buckets/{id}/deploy/sync";

        (await admin.PostAsJsonAsync(endpoint, new { files = Array.Empty<object>() }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(endpoint, new { files = new[] {
            new { path = "index.html", sha256 = hash }, new { path = "index.html", sha256 = hash } } },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(endpoint, new { files = new[] {
            new { path = "missing.html", sha256 = hash } } }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task<string> CreateBucketAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/buckets", new { name = $"deploy-{Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> UploadAsync(HttpClient client, string id, string path, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8);
        using var response = await client.PutAsync($"/api/buckets/{id}/upload/stream?filename={Uri.EscapeDataString(path)}",
            content, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("uploaded")[0].GetProperty("sha256").GetString()!;
    }

    private static async Task<string> CreateTokenAsync(HttpClient client, string id)
    {
        using var response = await client.PostAsJsonAsync($"/api/buckets/{id}/tokens", new { expires_in = "1h" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("token").GetString()!;
    }
}
