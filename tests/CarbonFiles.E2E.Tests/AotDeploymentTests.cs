using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.E2E.Tests;

public class AotDeploymentTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AotDeploymentTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _fixture.Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("\"status\"").And.Contain("\"uptime_seconds\"");
    }

    [Fact]
    public async Task OpenApi_Spec_IsAvailable()
    {
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        spec.Should().Contain("\"paths\"").And.Contain("/api/buckets");
    }

    [Fact]
    public async Task FullWorkflow_CrudOperations_WorkUnderAot()
    {
        using var admin = _fixture.CreateAdminClient();

        // === Create API key ===
        var keyResp = await admin.PostAsJsonAsync("/api/keys",
            new { name = "e2e-test" }, JsonOptions, TestContext.Current.CancellationToken);
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyJson = await keyResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var keyDoc = JsonDocument.Parse(keyJson);
        var apiKey = keyDoc.RootElement.GetProperty("key").GetString()!;
        var keyPrefix = keyDoc.RootElement.GetProperty("prefix").GetString()!;

        // Verify snake_case
        keyJson.Should().Contain("\"created_at\"");

        // === Create bucket ===
        using var keyClient = _fixture.CreateAuthenticatedClient(apiKey);
        var bucketResp = await keyClient.PostAsJsonAsync("/api/buckets",
            new { name = "e2e-bucket", expires_in = "1d" }, JsonOptions, TestContext.Current.CancellationToken);
        bucketResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketJson = await bucketResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var bucketDoc = JsonDocument.Parse(bucketJson);
        var bucketId = bucketDoc.RootElement.GetProperty("id").GetString()!;

        bucketJson.Should().Contain("\"file_count\"").And.Contain("\"total_size\"");

        // === Upload file ===
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent("hello AOT"u8.ToArray()), "files", "test.txt");
        var uploadResp = await keyClient.PostAsync($"/api/buckets/{bucketId}/upload",
            multipart, TestContext.Current.CancellationToken);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadJson = await uploadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        uploadJson.Should().Contain("\"short_code\"").And.Contain("\"mime_type\"");
        using var uploaded = JsonDocument.Parse(uploadJson);
        var hash = uploaded.RootElement.GetProperty("uploaded")[0].GetProperty("sha256").GetString()!;

        // === AOT source-generated deploy request and response ===
        var syncResp = await keyClient.PostAsJsonAsync($"/api/buckets/{bucketId}/deploy/sync",
            new { files = new[] { new { path = "test.txt", sha256 = hash } } }, JsonOptions,
            TestContext.Current.CancellationToken);
        syncResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await syncResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("\"kept\":1").And.Contain("\"deleted\":0");

        // === Download file ===
        var downloadResp = await _fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/test.txt/content", TestContext.Current.CancellationToken);
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await downloadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be("hello AOT");

        // === List files ===
        var listResp = await _fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files", TestContext.Current.CancellationToken);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        listJson.Should().Contain("\"items\"").And.Contain("\"total\"");

        // === Stats (admin) ===
        var statsResp = await admin.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsJson = await statsResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        statsJson.Should().Contain("\"total_buckets\"").And.Contain("\"total_files\"");

        // === Error responses serialize correctly ===
        var notFoundResp = await _fixture.Client.GetAsync("/api/buckets/nonexistent", TestContext.Current.CancellationToken);
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var forbiddenResp = await _fixture.Client.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var errorJson = await forbiddenResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        errorJson.Should().Contain("\"error\"");

        // === Cleanup ===
        (await keyClient.DeleteAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/keys/{keyPrefix}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
