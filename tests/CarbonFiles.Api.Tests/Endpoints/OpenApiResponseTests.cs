using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

/// <summary>
/// Verifies that every endpoint's .Produces() metadata is reflected in the
/// generated OpenAPI spec. Fetches /openapi/v1.json once and asserts that
/// the expected response status codes appear for each path + method.
/// </summary>
public class OpenApiResponseTests : IntegrationTestBase
{

    private async Task<JsonElement> GetSpecAsync()
    {
        var response = await Fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement GetOperation(JsonElement spec, string path, string method)
    {
        spec.TryGetProperty("paths", out var paths).Should().BeTrue($"spec should have 'paths'");
        paths.TryGetProperty(path, out var pathItem).Should().BeTrue($"spec should have path '{path}'");
        pathItem.TryGetProperty(method, out var operation).Should().BeTrue($"'{path}' should have method '{method}'");
        return operation;
    }

    private static void AssertResponseCodes(JsonElement operation, string path, string method, params string[] expectedCodes)
    {
        operation.TryGetProperty("responses", out var responses).Should()
            .BeTrue($"'{method.ToUpperInvariant()} {path}' should have 'responses'");

        var actualCodes = new List<string>();
        foreach (var prop in responses.EnumerateObject())
            actualCodes.Add(prop.Name);

        foreach (var code in expectedCodes)
        {
            actualCodes.Should().Contain(code,
                $"'{method.ToUpperInvariant()} {path}' should document response code {code}");
        }
    }

    // ── Health ──────────────────────────────────────────────────────

    [Fact]
    public async Task Health_Get_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/healthz", "get");
        AssertResponseCodes(op, "/healthz", "get", "200", "503");
    }

    // ── API Keys ────────────────────────────────────────────────────

    [Fact]
    public async Task Keys_Post_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/keys", "post");
        AssertResponseCodes(op, "/api/keys", "post", "201", "400", "403");
    }

    [Fact]
    public async Task Keys_Get_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/keys", "get");
        AssertResponseCodes(op, "/api/keys", "get", "200", "403");
    }

    [Fact]
    public async Task Keys_Delete_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/keys/{prefix}", "delete");
        AssertResponseCodes(op, "/api/keys/{prefix}", "delete", "204", "403", "404");
    }

    [Fact]
    public async Task Keys_GetUsage_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/keys/{prefix}/usage", "get");
        AssertResponseCodes(op, "/api/keys/{prefix}/usage", "get", "200", "403", "404");
    }

    // ── Buckets ─────────────────────────────────────────────────────

    [Fact]
    public async Task Buckets_Post_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets", "post");
        AssertResponseCodes(op, "/api/buckets", "post", "201", "400", "403");
    }

    [Fact]
    public async Task Buckets_Get_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets", "get");
        AssertResponseCodes(op, "/api/buckets", "get", "200", "403");
    }

    [Fact]
    public async Task Buckets_GetById_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}", "get");
        AssertResponseCodes(op, "/api/buckets/{id}", "get", "200", "404");
    }

    [Fact]
    public async Task Buckets_Patch_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}", "patch");
        AssertResponseCodes(op, "/api/buckets/{id}", "patch", "200", "400", "403", "404");
    }

    [Fact]
    public async Task Buckets_Delete_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}", "delete");
        AssertResponseCodes(op, "/api/buckets/{id}", "delete", "204", "403", "404");
    }

    [Fact]
    public async Task Buckets_GetSummary_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/summary", "get");
        AssertResponseCodes(op, "/api/buckets/{id}/summary", "get", "200", "404");
    }

    [Fact]
    public async Task Buckets_GetZip_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/zip", "get");
        AssertResponseCodes(op, "/api/buckets/{id}/zip", "get", "200", "404");
    }

    // ── Files ───────────────────────────────────────────────────────

    [Fact]
    public async Task Files_List_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/files", "get");
        AssertResponseCodes(op, "/api/buckets/{id}/files", "get", "200", "404");
    }

    [Fact]
    public async Task Files_GetOrDownload_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/files/{filePath}", "get");
        AssertResponseCodes(op, "/api/buckets/{id}/files/{filePath}", "get",
            "200", "206", "304", "404", "416");
    }

    [Fact]
    public async Task Files_Delete_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/files/{filePath}", "delete");
        AssertResponseCodes(op, "/api/buckets/{id}/files/{filePath}", "delete",
            "204", "403", "404");
    }

    [Fact]
    public async Task Files_PatchContent_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/files/{filePath}", "patch");
        AssertResponseCodes(op, "/api/buckets/{id}/files/{filePath}", "patch",
            "200", "400", "403", "404", "416");
    }

    // ── Uploads ─────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_Multipart_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/upload", "post");
        AssertResponseCodes(op, "/api/buckets/{id}/upload", "post",
            "201", "400", "403", "404", "413");
    }

    [Fact]
    public async Task Upload_Stream_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/upload/stream", "put");
        AssertResponseCodes(op, "/api/buckets/{id}/upload/stream", "put",
            "201", "400", "403", "404");
    }

    // ── Upload Tokens ───────────────────────────────────────────────

    [Fact]
    public async Task UploadTokens_Post_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/buckets/{id}/tokens", "post");
        AssertResponseCodes(op, "/api/buckets/{id}/tokens", "post",
            "201", "400", "403", "404");
    }

    // ── Dashboard Tokens ────────────────────────────────────────────

    [Fact]
    public async Task DashboardTokens_Post_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/tokens/dashboard", "post");
        AssertResponseCodes(op, "/api/tokens/dashboard", "post", "201", "400", "403");
    }

    [Fact]
    public async Task DashboardTokens_GetMe_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/tokens/dashboard/me", "get");
        AssertResponseCodes(op, "/api/tokens/dashboard/me", "get", "200", "401");
    }

    // ── Short URLs ──────────────────────────────────────────────────

    [Fact]
    public async Task ShortUrls_Resolve_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/s/{code}", "get");
        AssertResponseCodes(op, "/s/{code}", "get", "302", "404");
    }

    [Fact]
    public async Task ShortUrls_Delete_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/short/{code}", "delete");
        AssertResponseCodes(op, "/api/short/{code}", "delete", "204", "403", "404");
    }

    // ── Stats ───────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_Get_DocumentsResponseCodes()
    {
        var spec = await GetSpecAsync();
        var op = GetOperation(spec, "/api/stats", "get");
        AssertResponseCodes(op, "/api/stats", "get", "200", "403");
    }
}
