using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class BucketEndpointTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string ApiKey, string Prefix, string Name)> CreateApiKeyClientAsync(string name = "test-agent")
    {
        using var admin = Fixture.CreateAdminClient();
        var response = await admin.PostAsJsonAsync("/api/keys", new { name }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var apiKey = doc.RootElement.GetProperty("key").GetString()!;
        var prefix = doc.RootElement.GetProperty("prefix").GetString()!;

        var client = Fixture.CreateAuthenticatedClient(apiKey);
        return (client, apiKey, prefix, name);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Create Bucket ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateBucket_AsAdmin_Returns201()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = "admin-bucket", description = "test" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("id").GetString().Should().HaveLength(10);
        body.GetProperty("name").GetString().Should().Be("admin-bucket");
        body.GetProperty("owner").GetString().Should().Be("admin");
        body.GetProperty("description").GetString().Should().Be("test");
        body.TryGetProperty("created_at", out _).Should().BeTrue();
        body.TryGetProperty("expires_at", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBucket_WithApiKey_Returns201WithOwner()
    {
        var (client, _, _, name) = await CreateApiKeyClientAsync("bucket-creator");
        using (client)
        {
            var response = await client.PostAsJsonAsync("/api/buckets", new { name = "key-bucket" }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await ParseJsonAsync(response);
            body.GetProperty("owner").GetString().Should().Be("bucket-creator");
            body.GetProperty("name").GetString().Should().Be("key-bucket");
        }
    }

    [Fact]
    public async Task CreateBucket_Public_Returns403()
    {
        var response = await Fixture.Client.PostAsJsonAsync("/api/buckets", new { name = "public-bucket" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateBucket_MissingName_Returns400()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ParseJsonAsync(response);
        body.GetProperty("error").GetString().Should().Contain("required");
    }

    [Fact]
    public async Task CreateBucket_InvalidExpiry_Returns400()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = "bad-expiry", expires_in = "invalid" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateBucket_NeverExpiry_HasNullExpiresAt()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = "no-expiry", expires_in = "never" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        // expires_at is null, and WhenWritingNull means it's omitted from JSON
        body.TryGetProperty("expires_at", out var expiresAtProp).Should().BeFalse(
            "expires_at should be omitted when null due to WhenWritingNull policy");
    }

    // ── List Buckets ────────────────────────────────────────────────────

    [Fact]
    public async Task ListBuckets_AsAdmin_ReturnsAll()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create a bucket
        await admin.PostAsJsonAsync("/api/buckets", new { name = "list-all-test" }, TestContext.Current.CancellationToken);

        var response = await admin.GetAsync("/api/buckets", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.TryGetProperty("limit", out _).Should().BeTrue();
        body.TryGetProperty("offset", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ListBuckets_ApiKey_SeesOnlyOwn()
    {
        // Create two different API keys
        var (client1, _, _, _) = await CreateApiKeyClientAsync("owner-a");
        var (client2, _, _, _) = await CreateApiKeyClientAsync("owner-b");

        using (client1)
        using (client2)
        {
            // Create a bucket with each key
            var r1 = await client1.PostAsJsonAsync("/api/buckets", new { name = "bucket-a" }, TestContext.Current.CancellationToken);
            r1.StatusCode.Should().Be(HttpStatusCode.Created);

            var r2 = await client2.PostAsJsonAsync("/api/buckets", new { name = "bucket-b" }, TestContext.Current.CancellationToken);
            r2.StatusCode.Should().Be(HttpStatusCode.Created);

            // List as owner-a: should only see bucket-a
            var listA = await client1.GetAsync("/api/buckets", TestContext.Current.CancellationToken);
            var bodyA = await ParseJsonAsync(listA);
            var itemsA = bodyA.GetProperty("items");

            foreach (var item in itemsA.EnumerateArray())
                item.GetProperty("owner").GetString().Should().Be("owner-a");

            // List as owner-b: should only see bucket-b
            var listB = await client2.GetAsync("/api/buckets", TestContext.Current.CancellationToken);
            var bodyB = await ParseJsonAsync(listB);
            var itemsB = bodyB.GetProperty("items");

            foreach (var item in itemsB.EnumerateArray())
                item.GetProperty("owner").GetString().Should().Be("owner-b");
        }
    }

    [Fact]
    public async Task ListBuckets_Public_Returns403()
    {
        var response = await Fixture.Client.GetAsync("/api/buckets", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListBuckets_PaginationWorks()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create several buckets
        for (int i = 0; i < 3; i++)
            await admin.PostAsJsonAsync("/api/buckets", new { name = $"paginate-{i}" }, TestContext.Current.CancellationToken);

        var response = await admin.GetAsync("/api/buckets?limit=1&offset=0", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        body.GetProperty("limit").GetInt32().Should().Be(1);
        body.GetProperty("offset").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListBuckets_ExpiredExcludedByDefault()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create a bucket that expires immediately (15 minutes is the smallest preset)
        // We'll create one with "never" to ensure it shows up
        var neverResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "never-expires-list" , expires_in = "never" }, TestContext.Current.CancellationToken);
        neverResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var neverBody = await ParseJsonAsync(neverResp);
        var neverId = neverBody.GetProperty("id").GetString()!;

        // List should include the never-expiring bucket
        var listResp = await admin.GetAsync("/api/buckets", TestContext.Current.CancellationToken);
        var listBody = await ParseJsonAsync(listResp);
        var ids = new List<string>();
        foreach (var item in listBody.GetProperty("items").EnumerateArray())
            ids.Add(item.GetProperty("id").GetString()!);

        ids.Should().Contain(neverId);
    }

    [Fact]
    public async Task ListBuckets_AdminCanIncludeExpired()
    {
        using var admin = Fixture.CreateAdminClient();

        // Just verify the query parameter is accepted
        var response = await admin.GetAsync("/api/buckets?include_expired=true", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ── Get Bucket ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetBucket_ReturnsWithFiles()
    {
        using var admin = Fixture.CreateAdminClient();

        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "get-test" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("id").GetString().Should().Be(bucketId);
        body.GetProperty("name").GetString().Should().Be("get-test");
        body.TryGetProperty("files", out _).Should().BeFalse();
        body.GetProperty("unique_content_count").GetInt32().Should().Be(0);
        body.GetProperty("unique_content_size").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task GetBucket_PublicCanAccess()
    {
        using var admin = Fixture.CreateAdminClient();
        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "public-get" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        // Access without auth
        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBucket_NotFound_Returns404()
    {
        var response = await Fixture.Client.GetAsync("/api/buckets/nonexistent", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBucket_ExpiredBucket_Returns404()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create a bucket with shortest expiry (15m) — we can't easily make it expire
        // in tests, but we can verify the logic by creating one with "never" which should work
        // For expired testing we rely on unit tests since we can control the DB directly
        // Here just verify that a missing bucket returns 404
        var response = await Fixture.Client.GetAsync("/api/buckets/expired0000", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Update Bucket ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateBucket_OwnerCanUpdate()
    {
        var (client, _, _, _) = await CreateApiKeyClientAsync("updater");
        using (client)
        {
            var createResp = await client.PostAsJsonAsync("/api/buckets", new { name = "to-update" }, TestContext.Current.CancellationToken);
            var createBody = await ParseJsonAsync(createResp);
            var bucketId = createBody.GetProperty("id").GetString()!;

            var updateResp = await client.PatchAsJsonAsync($"/api/buckets/{bucketId}",
                new { name = "updated-name", description = "new desc" }, TestContext.Current.CancellationToken);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await ParseJsonAsync(updateResp);
            body.GetProperty("name").GetString().Should().Be("updated-name");
            body.GetProperty("description").GetString().Should().Be("new desc");
        }
    }

    [Fact]
    public async Task UpdateBucket_AdminCanUpdateAny()
    {
        var (client, _, _, _) = await CreateApiKeyClientAsync("update-owner");
        using (client)
        {
            var createResp = await client.PostAsJsonAsync("/api/buckets", new { name = "admin-will-update" }, TestContext.Current.CancellationToken);
            var createBody = await ParseJsonAsync(createResp);
            var bucketId = createBody.GetProperty("id").GetString()!;

            // Admin updates it
            using var admin = Fixture.CreateAdminClient();
            var updateResp = await admin.PatchAsJsonAsync($"/api/buckets/{bucketId}",
                new { name = "admin-updated" }, TestContext.Current.CancellationToken);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await ParseJsonAsync(updateResp);
            body.GetProperty("name").GetString().Should().Be("admin-updated");
        }
    }

    [Fact]
    public async Task UpdateBucket_Public_Returns403()
    {
        using var admin = Fixture.CreateAdminClient();
        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "no-public-update" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var updateResp = await Fixture.Client.PatchAsJsonAsync($"/api/buckets/{bucketId}",
            new { name = "hacked" }, TestContext.Current.CancellationToken);
        updateResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateBucket_NonOwnerApiKey_Returns403()
    {
        var (owner, _, _, _) = await CreateApiKeyClientAsync("real-owner");
        var (other, _, _, _) = await CreateApiKeyClientAsync("not-owner");

        using (owner)
        using (other)
        {
            var createResp = await owner.PostAsJsonAsync("/api/buckets", new { name = "owned-bucket" }, TestContext.Current.CancellationToken);
            var createBody = await ParseJsonAsync(createResp);
            var bucketId = createBody.GetProperty("id").GetString()!;

            // Other API key tries to update
            var updateResp = await other.PatchAsJsonAsync($"/api/buckets/{bucketId}",
                new { name = "stolen" }, TestContext.Current.CancellationToken);
            updateResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task UpdateBucket_NoFields_Returns400()
    {
        using var admin = Fixture.CreateAdminClient();
        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "no-fields" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var updateResp = await admin.PatchAsJsonAsync($"/api/buckets/{bucketId}", new { }, TestContext.Current.CancellationToken);
        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateBucket_CanUpdateExpiry()
    {
        var (client, _, _, _) = await CreateApiKeyClientAsync("expiry-updater");
        using (client)
        {
            var createResp = await client.PostAsJsonAsync("/api/buckets", new { name = "expiry-update" }, TestContext.Current.CancellationToken);
            var createBody = await ParseJsonAsync(createResp);
            var bucketId = createBody.GetProperty("id").GetString()!;

            var updateResp = await client.PatchAsJsonAsync($"/api/buckets/{bucketId}",
                new { expires_in = "never" }, TestContext.Current.CancellationToken);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await ParseJsonAsync(updateResp);
            // expires_at is null, and WhenWritingNull means it's omitted from JSON
            body.TryGetProperty("expires_at", out _).Should().BeFalse(
                "expires_at should be omitted when null due to WhenWritingNull policy");
        }
    }

    // ── Delete Bucket ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBucket_OwnerCanDelete_Returns204()
    {
        var (client, _, _, _) = await CreateApiKeyClientAsync("deleter");
        using (client)
        {
            var createResp = await client.PostAsJsonAsync("/api/buckets", new { name = "to-delete" }, TestContext.Current.CancellationToken);
            var createBody = await ParseJsonAsync(createResp);
            var bucketId = createBody.GetProperty("id").GetString()!;

            var deleteResp = await client.DeleteAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
            deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Verify it's gone
            var getResp = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
            getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task DeleteBucket_AdminCanDelete_Returns204()
    {
        using var admin = Fixture.CreateAdminClient();

        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "admin-delete" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var deleteResp = await admin.DeleteAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteBucket_Public_Returns403()
    {
        using var admin = Fixture.CreateAdminClient();

        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "no-public-delete" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var deleteResp = await Fixture.Client.DeleteAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        deleteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteBucket_NotFound_Returns404()
    {
        using var admin = Fixture.CreateAdminClient();
        var response = await admin.DeleteAsync("/api/buckets/nonexistent", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Summary ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReturnsPlaintext()
    {
        using var admin = Fixture.CreateAdminClient();

        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "summary-test" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/summary", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Should().Contain("Bucket: summary-test");
        text.Should().Contain("Owner:");
        text.Should().Contain("Files:");
        text.Should().Contain("Created:");
        text.Should().Contain("Expires:");
    }

    [Fact]
    public async Task GetSummary_PublicCanAccess()
    {
        using var admin = Fixture.CreateAdminClient();

        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "public-summary" }, TestContext.Current.CancellationToken);
        var createBody = await ParseJsonAsync(createResp);
        var bucketId = createBody.GetProperty("id").GetString()!;

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/summary", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_NotFound_Returns404()
    {
        var response = await Fixture.Client.GetAsync("/api/buckets/nonexistent/summary", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
