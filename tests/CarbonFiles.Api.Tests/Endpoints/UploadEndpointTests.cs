using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class UploadEndpointTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient? client = null)
    {
        var c = client ?? Fixture.CreateAdminClient();
        var response = await c.PostAsJsonAsync("/api/buckets", new { name = $"upload-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent CreateMultipartContent(string fieldName, string fileName, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, fieldName, fileName);
        return multipart;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Multipart Upload ────────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_AsAdmin_Returns201WithFileInfo()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("file", "hello.txt", "Hello, World!");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var uploaded = body.GetProperty("uploaded");
        uploaded.GetArrayLength().Should().Be(1);

        var file = uploaded[0];
        file.GetProperty("path").GetString().Should().Be("hello.txt");
        file.GetProperty("name").GetString().Should().Be("hello.txt");
        file.GetProperty("size").GetInt64().Should().Be(13); // "Hello, World!" = 13 bytes
        file.GetProperty("mime_type").GetString().Should().Be("text/plain");
        file.TryGetProperty("short_code", out var sc).Should().BeTrue();
        sc.GetString().Should().HaveLength(6);
        file.TryGetProperty("short_url", out var su).Should().BeTrue();
        su.GetString().Should().StartWith("/s/");
    }

    [Fact]
    public async Task MultipartUpload_MultipleFiles_ReturnsAll()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var multipart = new MultipartFormDataContent();

        var file1 = new ByteArrayContent(Encoding.UTF8.GetBytes("file one"));
        file1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file1, "file", "one.txt");

        var file2 = new ByteArrayContent(Encoding.UTF8.GetBytes("file two"));
        file2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file2, "file", "two.txt");

        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("uploaded").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task MultipartUpload_CustomFieldName_SetsPath()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("src/main.rs", "ignored.txt", "fn main() {}");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("src/main.rs");
    }

    [Fact]
    public async Task MultipartUpload_GenericFieldName_UsesFileName()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("upload", "data.json", "{}");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("data.json");
    }

    [Fact]
    public async Task MultipartUpload_WithoutAuth_Returns403()
    {
        using var adminClient = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(adminClient);

        using var content = CreateMultipartContent("file", "test.txt", "test");
        var response = await Fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MultipartUpload_ReuploadSamePath_Overwrites()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload first version
        using var content1 = CreateMultipartContent("file", "doc.txt", "version 1");
        var response1 = await client.PostAsync($"/api/buckets/{bucketId}/upload", content1, TestContext.Current.CancellationToken);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        var body1 = await ParseJsonAsync(response1);
        var shortCode1 = body1.GetProperty("uploaded")[0].GetProperty("short_code").GetString();

        // Upload second version with same filename
        using var content2 = CreateMultipartContent("file", "doc.txt", "version 2 is longer");
        var response2 = await client.PostAsync($"/api/buckets/{bucketId}/upload", content2, TestContext.Current.CancellationToken);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var body2 = await ParseJsonAsync(response2);
        var file2 = body2.GetProperty("uploaded")[0];
        file2.GetProperty("size").GetInt64().Should().Be(Encoding.UTF8.GetByteCount("version 2 is longer"));

        // Short code should be preserved
        file2.GetProperty("short_code").GetString().Should().Be(shortCode1);
    }

    [Fact]
    public async Task MultipartUpload_NonexistentBucket_Returns404()
    {
        using var client = Fixture.CreateAdminClient();

        using var content = CreateMultipartContent("file", "test.txt", "test");
        var response = await client.PostAsync("/api/buckets/nonexistent/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Stream Upload ───────────────────────────────────────────────────

    [Fact]
    public async Task StreamUpload_WithFilename_Returns201()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("stream content"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PutAsync($"/api/buckets/{bucketId}/upload/stream?filename=stream.txt", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var uploaded = body.GetProperty("uploaded");
        uploaded.GetArrayLength().Should().Be(1);
        uploaded[0].GetProperty("path").GetString().Should().Be("stream.txt");
        uploaded[0].GetProperty("mime_type").GetString().Should().Be("text/plain");
    }

    [Fact]
    public async Task StreamUpload_MissingFilename_Returns400()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        var response = await client.PutAsync($"/api/buckets/{bucketId}/upload/stream", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StreamUpload_WithoutAuth_Returns403()
    {
        using var adminClient = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(adminClient);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        var response = await Fixture.Client.PutAsync($"/api/buckets/{bucketId}/upload/stream?filename=test.txt", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Upload with API Key ─────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_WithApiKey_Works()
    {
        using var admin = Fixture.CreateAdminClient();

        // Create an API key
        var keyResp = await admin.PostAsJsonAsync("/api/keys", new { name = "uploader" }, TestContext.Current.CancellationToken);
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = await ParseJsonAsync(keyResp);
        var apiKey = keyBody.GetProperty("key").GetString()!;

        using var apiClient = Fixture.CreateAuthenticatedClient(apiKey);

        // Create a bucket with this key
        var bucketResp = await apiClient.PostAsJsonAsync("/api/buckets", new { name = "key-upload-test" }, TestContext.Current.CancellationToken);
        bucketResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketBody = await ParseJsonAsync(bucketResp);
        var bucketId = bucketBody.GetProperty("id").GetString()!;

        // Upload with the API key
        using var content = CreateMultipartContent("file", "key-file.txt", "uploaded with key");
        var response = await apiClient.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Nested Path Uploads ──────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_NestedPath_PreservesPathAndName()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("file", "src/nested/test.txt", "nested content");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("src/nested/test.txt");
        file.GetProperty("name").GetString().Should().Be("test.txt");
    }

    [Fact]
    public async Task MultipartUpload_NestedPath_MetadataRetrievable()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("file", "src/utils/helper.cs", "class Helper {}");
        var uploadResp = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Retrieve metadata using the nested path (not URL-encoded slashes)
        var metaResp = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/src/utils/helper.cs", TestContext.Current.CancellationToken);
        metaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await ParseJsonAsync(metaResp);
        meta.GetProperty("path").GetString().Should().Be("src/utils/helper.cs");
        meta.GetProperty("name").GetString().Should().Be("helper.cs");
    }

    [Fact]
    public async Task MultipartUpload_NestedPath_ContentDownloadable()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var fileContent = "deeply nested content";

        using var content = CreateMultipartContent("file", "a/b/c/deep.txt", fileContent);
        var uploadResp = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var downloadResp = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/a/b/c/deep.txt/content", TestContext.Current.CancellationToken);
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var downloaded = await downloadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        downloaded.Should().Be(fileContent);
    }

    [Fact]
    public async Task MultipartUpload_NestedPath_AppearsInFileListing()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content1 = CreateMultipartContent("file", "src/main.cs", "main");
        await client.PostAsync($"/api/buckets/{bucketId}/upload", content1, TestContext.Current.CancellationToken);

        using var content2 = CreateMultipartContent("file", "src/utils/helper.cs", "helper");
        await client.PostAsync($"/api/buckets/{bucketId}/upload", content2, TestContext.Current.CancellationToken);

        using var content3 = CreateMultipartContent("file", "root.txt", "root");
        await client.PostAsync($"/api/buckets/{bucketId}/upload", content3, TestContext.Current.CancellationToken);

        var listResp = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files?limit=50", TestContext.Current.CancellationToken);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(listResp);
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        var paths = new HashSet<string>();
        for (int i = 0; i < items.GetArrayLength(); i++)
            paths.Add(items[i].GetProperty("path").GetString()!);

        paths.Should().Contain("src/main.cs");
        paths.Should().Contain("src/utils/helper.cs");
        paths.Should().Contain("root.txt");
    }

    [Fact]
    public async Task MultipartUpload_NestedPath_Deletable()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("file", "docs/readme.md", "# Readme");
        await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        var deleteResp = await client.DeleteAsync(
            $"/api/buckets/{bucketId}/files/docs/readme.md", TestContext.Current.CancellationToken);
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metaResp = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/docs/readme.md", TestContext.Current.CancellationToken);
        metaResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StreamUpload_NestedPath_FullRoundTrip()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var fileContent = "stream nested content";

        var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var encodedFilename = Uri.EscapeDataString("lib/stream/data.bin");

        var uploadResp = await client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename={encodedFilename}", byteContent, TestContext.Current.CancellationToken);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(uploadResp);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("lib/stream/data.bin");
        file.GetProperty("name").GetString().Should().Be("data.bin");

        // Verify content downloadable
        var downloadResp = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/lib/stream/data.bin/content", TestContext.Current.CancellationToken);
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var downloaded = await downloadResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        downloaded.Should().Be(fileContent);
    }

    // ── CAS Deduplication ──────────────────────────────────────────────

    private async Task<JsonElement> UploadFileAsync(HttpClient client, string bucketId, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await ParseJsonAsync(response);
        return body.GetProperty("uploaded")[0].Clone();
    }

    [Fact]
    public async Task Upload_ReturnsSha256Hash()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var file = await UploadFileAsync(client, bucketId, "test.txt", "hello world");

        file.GetProperty("sha256").GetString().Should().NotBeNullOrEmpty();
        file.GetProperty("sha256").GetString()!.Length.Should().Be(64);
    }

    [Fact]
    public async Task Upload_IdenticalContent_Deduplicates()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucketId, "file1.txt", "identical content");
        var file2 = await UploadFileAsync(client, bucketId, "file2.txt", "identical content");

        file1.GetProperty("sha256").GetString().Should().Be(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Upload_DifferentContent_NoDeduplicate()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucketId, "a.txt", "content A");
        var file2 = await UploadFileAsync(client, bucketId, "b.txt", "content B");

        file1.GetProperty("sha256").GetString().Should().NotBe(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Upload_SamePathOverwrite_UpdatesHash()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "file.txt", "version 1");
        var file2 = await UploadFileAsync(client, bucketId, "file.txt", "version 2");

        file2.GetProperty("sha256").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Upload_CrossBucketDedup()
    {
        var client = Fixture.CreateAdminClient();
        var bucket1 = await CreateBucketAsync(client);
        var bucket2 = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucket1, "shared.txt", "shared content");
        var file2 = await UploadFileAsync(client, bucket2, "copy.txt", "shared content");

        file1.GetProperty("sha256").GetString().Should().Be(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Delete_DecreasesRefCount_ContentSurvivesIfShared()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "file1.txt", "shared content for delete");
        await UploadFileAsync(client, bucketId, "file2.txt", "shared content for delete");

        var deleteResponse = await client.DeleteAsync(
            $"/api/buckets/{bucketId}/files/file1.txt",
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var downloadResponse = await client.GetAsync(
            $"/api/buckets/{bucketId}/files/file2.txt/content",
            TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be("shared content for delete");
    }

    [Fact]
    public async Task Delete_LastReference_CrossBucket_ContentSurvives()
    {
        var client = Fixture.CreateAdminClient();
        var bucket1 = await CreateBucketAsync(client);
        var bucket2 = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucket1, "file.txt", "unique content for delete test");
        await UploadFileAsync(client, bucket2, "file.txt", "unique content for delete test");

        await client.DeleteAsync($"/api/buckets/{bucket1}/files/file.txt",
            TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/api/buckets/{bucket2}/files/file.txt/content",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Custom Field Name ───────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_CustomFieldNameWithSlashes_UsesFieldNameAsPath()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Non-generic field name with slashes → used as the file path
        using var content = CreateMultipartContent("config/app/settings.json", "ignored.txt", "{\"key\":\"value\"}");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("config/app/settings.json");
        file.GetProperty("name").GetString().Should().Be("settings.json");
    }

    // ── Path Normalization ──────────────────────────────────────────────

    [Fact]
    public async Task Upload_NormalizesBackslashPath()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("content"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "src\\utils\\helper.cs", "helper.cs");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await ParseJsonAsync(response);
        var path = body.GetProperty("uploaded")[0].GetProperty("path").GetString();

        path.Should().Be("src/utils/helper.cs");
    }

    [Fact]
    public async Task Upload_PathTraversal_Returns400()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("content"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "../etc/passwd", "passwd");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
