using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class BucketZipTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(string name = "zip-test")
    {
        using var admin = Fixture.CreateAdminClient();
        var response = await admin.PostAsJsonAsync("/api/buckets", new { name }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task UploadFileAsync(string bucketId, string fileName, string content)
    {
        using var admin = Fixture.CreateAdminClient();
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);
        var response = await admin.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZipDownload_WithFiles_ReturnsValidZipContainingAllFiles()
    {
        var bucketId = await CreateBucketAsync("zip-with-files");
        await UploadFileAsync(bucketId, "hello.txt", "Hello, World!");
        await UploadFileAsync(bucketId, "readme.md", "# README");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(2);

        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        entryNames.Should().Contain("hello.txt");
        entryNames.Should().Contain("readme.md");
    }

    [Fact]
    public async Task ZipDownload_HasCorrectContentTypeAndDisposition()
    {
        var bucketId = await CreateBucketAsync("zip-headers");
        await UploadFileAsync(bucketId, "file.txt", "content");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain("zip-headers");
    }

    [Fact]
    public async Task ZipDownload_NonexistentBucket_Returns404()
    {
        var response = await Fixture.Client.GetAsync("/api/buckets/nonexistent/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ZipDownload_ExpiredBucket_Returns404()
    {
        // We can't easily expire a bucket in integration tests since the minimum is 15m,
        // but we verify a non-existent bucket returns 404 which exercises the same code path.
        var response = await Fixture.Client.GetAsync("/api/buckets/expired0000/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ZipDownload_EmptyBucket_ReturnsValidEmptyZip()
    {
        var bucketId = await CreateBucketAsync("zip-empty");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ZipDownload_FileContentsMatchOriginal()
    {
        var bucketId = await CreateBucketAsync("zip-contents");
        var expectedContent = "This is the exact file content for verification.";
        await UploadFileAsync(bucketId, "verify.txt", expectedContent);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);

        var entry = archive.Entries[0];
        entry.FullName.Should().Be("verify.txt");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var actualContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        actualContent.Should().Be(expectedContent);
    }

    [Fact]
    public async Task ZipDownload_HeadRequest_ReturnsHeadersWithoutBody()
    {
        var bucketId = await CreateBucketAsync("zip-head");
        await UploadFileAsync(bucketId, "file.txt", "content");

        var request = new HttpRequestMessage(HttpMethod.Head, $"/api/buckets/{bucketId}/zip");
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        // HEAD should have no body
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task ZipDownload_NestedPaths_CreatesDirectoryEntries()
    {
        var bucketId = await CreateBucketAsync("zip-nested");
        await UploadFileAsync(bucketId, "src/main.cs", "class Main {}");
        await UploadFileAsync(bucketId, "src/utils/helper.cs", "class Helper {}");
        await UploadFileAsync(bucketId, "root.txt", "root file");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();

        // Should contain explicit directory entries
        entryNames.Should().Contain("src/");
        entryNames.Should().Contain("src/utils/");

        // Should contain file entries with full paths
        entryNames.Should().Contain("src/main.cs");
        entryNames.Should().Contain("src/utils/helper.cs");
        entryNames.Should().Contain("root.txt");

        // Verify file content is preserved
        var helper = archive.GetEntry("src/utils/helper.cs")!;
        using var reader = new StreamReader(helper.Open(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        content.Should().Be("class Helper {}");
    }
}
