using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

/// <summary>
/// Tests that filenames with emojis, unicode, spaces, special URL characters,
/// and other non-ASCII characters work correctly through the full lifecycle:
/// upload -> list -> get metadata -> download content -> verify content -> delete.
///
/// The API stores files on disk using URL-encoded paths. Original filenames
/// (including case) are preserved in API responses.
/// </summary>
public class SpecialCharacterFileTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient? client = null, string? name = null)
    {
        var c = client ?? Fixture.CreateAdminClient();
        var bucketName = name ?? $"special-char-test-{Guid.NewGuid():N}";
        var response = await c.PostAsJsonAsync("/api/buckets", new { name = bucketName }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent CreateMultipartContent(
        string fieldName, string fileName, string content)
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

    /// <summary>
    /// Uploads a file via multipart and returns the uploaded file JSON element.
    /// </summary>
    private async Task<JsonElement> UploadFileAsync(
        HttpClient client, string bucketId, string fileName, string content)
    {
        using var multipart = CreateMultipartContent("file", fileName, content);
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Upload should succeed for filename '{fileName}'");
        var body = await ParseJsonAsync(response);
        return body.GetProperty("uploaded")[0].Clone();
    }

    /// <summary>
    /// Uploads a file via stream endpoint and returns the uploaded file JSON element.
    /// </summary>
    private async Task<JsonElement> StreamUploadFileAsync(
        HttpClient client, string bucketId, string fileName, string content)
    {
        var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var encodedFilename = Uri.EscapeDataString(fileName);
        var response = await client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename={encodedFilename}", byteContent, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Stream upload should succeed for filename '{fileName}'");
        var body = await ParseJsonAsync(response);
        return body.GetProperty("uploaded")[0].Clone();
    }

    /// <summary>
    /// Runs the full lifecycle test for a given filename:
    /// upload -> metadata -> download -> list -> short URL -> delete.
    /// Returns details about any step that fails expectations.
    /// </summary>
    private async Task RunFullLifecycleAsync(string fileName, string fileContent)
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // The API preserves the original path case
        var expectedPath = fileName;
        var expectedName = Path.GetFileName(fileName);
        var expectedSize = (long)Encoding.UTF8.GetByteCount(fileContent);

        // ── Step 1: Upload ──
        var uploaded = await UploadFileAsync(client, bucketId, fileName, fileContent);

        var uploadedPath = uploaded.GetProperty("path").GetString();
        uploadedPath.Should().Be(expectedPath,
            $"Uploaded file path should be the original path for '{fileName}'");

        var uploadedName = uploaded.GetProperty("name").GetString();
        uploadedName.Should().Be(expectedName,
            $"Uploaded file name should be the original filename for '{fileName}'");

        uploaded.GetProperty("size").GetInt64().Should().Be(expectedSize,
            $"Uploaded file size should match content length for '{fileName}'");

        uploaded.TryGetProperty("short_code", out var shortCodeProp).Should().BeTrue(
            $"Upload response should include short_code for '{fileName}'");
        var shortCode = shortCodeProp.GetString()!;
        shortCode.Should().HaveLength(6,
            $"Short code should be 6 chars for '{fileName}'");

        uploaded.TryGetProperty("short_url", out var shortUrlProp).Should().BeTrue(
            $"Upload response should include short_url for '{fileName}'");
        shortUrlProp.GetString().Should().StartWith("/s/",
            $"Short URL should start with /s/ for '{fileName}'");

        // ── Step 2: Get metadata ──
        // We need to URL-encode the path for the HTTP request
        var encodedPath = Uri.EscapeDataString(expectedPath);
        var metaResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}", TestContext.Current.CancellationToken);
        metaResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Metadata GET should return 200 for '{fileName}' (encoded as '{encodedPath}')");

        var meta = await ParseJsonAsync(metaResponse);
        meta.GetProperty("path").GetString().Should().Be(expectedPath,
            $"Metadata path should be the original path for '{fileName}'");
        meta.GetProperty("name").GetString().Should().Be(expectedName,
            $"Metadata name should be the original filename for '{fileName}'");
        meta.GetProperty("size").GetInt64().Should().Be(expectedSize,
            $"Metadata size should match for '{fileName}'");

        // ── Step 3: Download content ──
        var downloadResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content", TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Content download should return 200 for '{fileName}'");

        var downloadedContent = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        downloadedContent.Should().Be(fileContent,
            $"Downloaded content should match uploaded content for '{fileName}'");

        // ── Step 4: List files ──
        var listResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files", TestContext.Current.CancellationToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"File listing should return 200 for '{fileName}'");

        var listBody = await ParseJsonAsync(listResponse);
        var items = listBody.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            $"File listing should contain at least 1 file for '{fileName}'");

        // Find our file in the listing
        var foundInList = false;
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            if (items[i].GetProperty("path").GetString() == expectedPath)
            {
                foundInList = true;
                items[i].GetProperty("name").GetString().Should().Be(expectedName,
                    $"Listed file name should match for '{fileName}'");
                break;
            }
        }
        foundInList.Should().BeTrue(
            $"File with path '{expectedPath}' should appear in the listing for '{fileName}'");

        // ── Step 5: Short URL resolves ──
        using var noRedirectClient = Fixture.CreateNoRedirectClient();
        var shortUrlResponse = await noRedirectClient.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);
        shortUrlResponse.StatusCode.Should().Be(HttpStatusCode.Redirect,
            $"Short URL should redirect (302) for '{fileName}'");

        var redirectLocation = shortUrlResponse.Headers.Location?.ToString();
        redirectLocation.Should().NotBeNullOrEmpty(
            $"Short URL redirect should have a Location header for '{fileName}'");
        // The redirect target should contain the bucket ID and point to the content endpoint
        redirectLocation.Should().Contain(bucketId,
            $"Short URL redirect should reference the correct bucket for '{fileName}'");
        redirectLocation.Should().EndWith("/content",
            $"Short URL redirect should point to the /content endpoint for '{fileName}'");

        // ── Step 6: Delete ──
        var deleteResponse = await client.DeleteAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}", TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"Delete should return 204 for '{fileName}'");

        // Verify it's gone
        var afterDeleteResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}", TestContext.Current.CancellationToken);
        afterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"File should be gone after delete for '{fileName}'");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Emoji Filenames ────────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmojiFilename_PartyEmoji_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "\U0001f389party.txt",
            "Party time content!");
    }

    [Fact]
    public async Task EmojiFilename_FolderAndDocumentEmojis_FullLifecycle()
    {
        // Note: The API stores path separators as part of the path string
        await RunFullLifecycleAsync(
            "\U0001f4c4document.md",
            "# Document with emoji filename");
    }

    [Fact]
    public async Task EmojiFilename_HelloWorld_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "hello \U0001f30d.txt",
            "Hello world emoji content");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Unicode Filenames ──────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnicodeFilename_Japanese_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "\u65E5\u672C\u8A9E\u30D5\u30A1\u30A4\u30EB.txt",
            "Japanese filename content");
    }

    [Fact]
    public async Task UnicodeFilename_FrenchAccents_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "donn\u00E9es.csv",
            "col1,col2\nval1,val2");
    }

    [Fact]
    public async Task UnicodeFilename_GermanUmlaut_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "\u00FCber-file.txt",
            "German umlaut content");
    }

    [Fact]
    public async Task UnicodeFilename_SpanishTilde_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "\u00D1o\u00F1o.txt",
            "Spanish tilde content");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Spaces in Filenames ────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SpacesInFilename_SimpleSpace_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "my file.txt",
            "File with spaces in name");
    }

    [Fact]
    public async Task SpacesInFilename_MultipleSpaces_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "my  double  spaced  file.txt",
            "File with multiple spaces");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Special URL Characters ─────────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SpecialUrlChars_Plus_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "file+plus.txt",
            "Plus sign content");
    }

    [Fact]
    public async Task SpecialUrlChars_Hash_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "file#hash.txt",
            "Hash sign content");
    }

    [Fact]
    public async Task SpecialUrlChars_Ampersand_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "file&amp.txt",
            "Ampersand content");
    }

    [Fact]
    public async Task SpecialUrlChars_Percent_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "100%.txt",
            "Percent sign content");
    }

    [Fact]
    public async Task SpecialUrlChars_QuestionMark_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "file?query.txt",
            "Question mark content");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Dots and Extensions ────────────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DotsInFilename_MultipleDots_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "file.name.with.dots.txt",
            "Multiple dots content");
    }

    [Fact]
    public async Task DotsInFilename_HiddenFile_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            ".hidden",
            "Hidden file content");
    }

    [Fact]
    public async Task DotsInFilename_NoExtension_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "no-extension",
            "No extension content");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Mixed Special Characters ───────────────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedSpecialChars_ResumeWithEmoji_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "r\u00E9sum\u00E9 (2024) \u2014 final copy \U0001F4CE.pdf",
            "Mixed special character content");
    }

    [Fact]
    public async Task MixedSpecialChars_UnicodeAndSpaces_FullLifecycle()
    {
        await RunFullLifecycleAsync(
            "caf\u00E9 menu \U0001F375.txt",
            "Unicode and emoji mix");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Stream Upload with Special Characters ──────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StreamUpload_EmojiFilename_WorksCorrectly()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var fileName = "\U0001f389stream-party.txt";
        var fileContent = "Stream uploaded emoji file";
        var expectedPath = fileName;

        var uploaded = await StreamUploadFileAsync(client, bucketId, fileName, fileContent);

        uploaded.GetProperty("path").GetString().Should().Be(expectedPath,
            "Stream upload should preserve emoji filename (case-preserved) in path");
        uploaded.GetProperty("name").GetString().Should().Be(Path.GetFileName(fileName),
            "Stream upload should preserve emoji filename in name");

        // Verify content can be downloaded
        var encodedPath = Uri.EscapeDataString(expectedPath);
        var downloadResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content", TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "Content should be downloadable for stream-uploaded emoji file");
        var content = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be(fileContent);
    }

    [Fact]
    public async Task StreamUpload_UnicodeFilename_WorksCorrectly()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var fileName = "donn\u00E9es-stream.csv";
        var fileContent = "a,b\n1,2";
        var expectedPath = fileName;

        var uploaded = await StreamUploadFileAsync(client, bucketId, fileName, fileContent);

        uploaded.GetProperty("path").GetString().Should().Be(expectedPath,
            "Stream upload should preserve unicode filename (case-preserved) in path");

        // Verify content
        var encodedPath = Uri.EscapeDataString(expectedPath);
        var downloadResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content", TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be(fileContent);
    }

    [Fact]
    public async Task StreamUpload_SpacesInFilename_WorksCorrectly()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var fileName = "my stream file.txt";
        var fileContent = "Stream uploaded file with spaces";
        var expectedPath = fileName;

        var uploaded = await StreamUploadFileAsync(client, bucketId, fileName, fileContent);

        uploaded.GetProperty("path").GetString().Should().Be(expectedPath,
            "Stream upload should preserve spaces in path");

        var encodedPath = Uri.EscapeDataString(expectedPath);
        var downloadResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content", TestContext.Current.CancellationToken);
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be(fileContent);
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Multipart Upload with Custom Field Names ───────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipartUpload_CustomFieldNameWithSpecialChars_SetsPath()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // When field name is not in the generic set, it is used as the path
        var fieldName = "docs/r\u00E9sum\u00E9.pdf";
        using var content = CreateMultipartContent(fieldName, "ignored.txt", "custom field content");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "Upload with special char field name should succeed");

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        // The field name becomes the path
        file.GetProperty("path").GetString().Should().Be(fieldName,
            "Custom field name with special chars should be used as the path (case-preserved)");
    }

    [Fact]
    public async Task MultipartUpload_CustomFieldNameWithEmoji_SetsPath()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fieldName = "\U0001f4c1data/report.txt";
        using var content = CreateMultipartContent(fieldName, "ignored.txt", "emoji field content");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "Upload with emoji field name should succeed");

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be(fieldName,
            "Custom field name with emojis should be used as the path (case-preserved)");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Bucket Summary with Special Filenames ──────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BucketSummary_WithSpecialCharFiles_CorrectFileCountAndSize()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var files = new (string Name, string Content)[]
        {
            ("\U0001f389party.txt", "emoji content"),
            ("donn\u00E9es.csv", "unicode content"),
            ("my file.txt", "spaces content"),
            ("file+plus.txt", "plus content"),
        };

        long totalSize = 0;
        foreach (var (name, content) in files)
        {
            await UploadFileAsync(client, bucketId, name, content);
            totalSize += Encoding.UTF8.GetByteCount(content);
        }

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("file_count").GetInt32().Should().Be(files.Length,
            "Bucket should have correct file count after uploading special char files");
        body.GetProperty("total_size").GetInt64().Should().Be(totalSize,
            "Bucket should have correct total size after uploading special char files");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── ZIP Download with Special Filenames ────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ZipDownload_WithEmojiFilenames_PreservesNamesInArchive()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client, "zip-emoji-test");

        await UploadFileAsync(client, bucketId, "\U0001f389party.txt", "party content");
        await UploadFileAsync(client, bucketId, "normal.txt", "normal content");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(2);

        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        // ZIP entries use the DB path which preserves original case
        entryNames.Should().Contain("normal.txt");
        entryNames.Should().Contain("\U0001f389party.txt",
            "ZIP archive should contain the emoji filename (case-preserved)");
    }

    [Fact]
    public async Task ZipDownload_WithUnicodeFilenames_PreservesNamesAndContent()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client, "zip-unicode-test");

        var expectedContent = "Contenu fran\u00E7ais";
        await UploadFileAsync(client, bucketId, "donn\u00E9es.csv", expectedContent);

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);

        var entry = archive.Entries[0];
        entry.FullName.Should().Be("donn\u00E9es.csv",
            "ZIP entry should have the unicode filename (case-preserved)");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var actualContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        actualContent.Should().Be(expectedContent,
            "ZIP entry content should match the uploaded content");
    }

    [Fact]
    public async Task ZipDownload_WithSpacesInFilenames_PreservesNames()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client, "zip-spaces-test");

        await UploadFileAsync(client, bucketId, "my file.txt", "spaces content");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);

        archive.Entries[0].FullName.Should().Be("my file.txt",
            "ZIP entry should have the filename with spaces");
    }

    [Fact]
    public async Task ZipDownload_WithSpecialUrlChars_PreservesNames()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client, "zip-urlchars-test");

        await UploadFileAsync(client, bucketId, "file+plus.txt", "plus content");

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);

        archive.Entries[0].FullName.Should().Be("file+plus.txt",
            "ZIP entry should have the filename with special URL characters");
    }

    [Fact]
    public async Task ZipDownload_MixedSpecialCharFilenames_AllPresent()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client, "zip-mixed-test");

        var fileNames = new[]
        {
            "\U0001f389party.txt",
            "donn\u00E9es.csv",
            "my file.txt",
            "file+plus.txt",
            "file.name.with.dots.txt",
        };

        foreach (var name in fileNames)
        {
            await UploadFileAsync(client, bucketId, name, $"content for {name}");
        }

        var response = await Fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(fileNames.Length,
            "ZIP should contain all uploaded files");

        var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();
        foreach (var name in fileNames)
        {
            var expectedName = name;
            entryNames.Should().Contain(expectedName,
                $"ZIP should contain entry '{expectedName}'");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Short URL with Special Filenames ───────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShortUrl_EmojiFilename_RedirectsAndServesContent()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "\U0001f389party.txt";
        var fileContent = "Short URL emoji content";
        var uploaded = await UploadFileAsync(client, bucketId, fileName, fileContent);
        var shortCode = uploaded.GetProperty("short_code").GetString()!;

        // Follow the short URL redirect to get content
        var contentResponse = await Fixture.Client.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);
        contentResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "Following the short URL redirect should serve the file content");

        var downloadedContent = await contentResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        downloadedContent.Should().Be(fileContent,
            "Short URL should serve the correct content for emoji filename");
    }

    [Fact]
    public async Task ShortUrl_UnicodeFilename_RedirectsAndServesContent()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "donn\u00E9es.csv";
        var fileContent = "a,b\n1,2";
        var uploaded = await UploadFileAsync(client, bucketId, fileName, fileContent);
        var shortCode = uploaded.GetProperty("short_code").GetString()!;

        var contentResponse = await Fixture.Client.GetAsync($"/s/{shortCode}", TestContext.Current.CancellationToken);
        contentResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "Following the short URL redirect should serve the file content");

        var downloadedContent = await contentResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        downloadedContent.Should().Be(fileContent,
            "Short URL should serve the correct content for unicode filename");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Overwrite / Re-upload with Special Chars ───────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reupload_EmojiFilename_OverwritesAndPreservesShortCode()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "\U0001f389party.txt";

        // Upload first version
        var uploaded1 = await UploadFileAsync(client, bucketId, fileName, "version 1");
        var shortCode1 = uploaded1.GetProperty("short_code").GetString()!;

        // Upload second version
        var uploaded2 = await UploadFileAsync(client, bucketId, fileName, "version 2 is longer");
        var shortCode2 = uploaded2.GetProperty("short_code").GetString()!;

        shortCode2.Should().Be(shortCode1,
            "Short code should be preserved on re-upload for emoji filename");

        uploaded2.GetProperty("size").GetInt64()
            .Should().Be(Encoding.UTF8.GetByteCount("version 2 is longer"),
            "Size should be updated for re-uploaded emoji file");

        // Verify new content is served
        var encodedPath = Uri.EscapeDataString(fileName);
        var downloadResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content", TestContext.Current.CancellationToken);
        var content = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Be("version 2 is longer",
            "Downloaded content should be the latest version for emoji filename");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Content-Disposition with Special Chars ─────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_EmojiFilename_ContentDispositionPreservesName()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "\U0001f389party.txt";
        await UploadFileAsync(client, bucketId, fileName, "party content");

        var encodedPath = Uri.EscapeDataString(fileName);
        var response = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content?download=true", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Download with ?download=true should work for emoji filename");

        response.Content.Headers.ContentDisposition.Should().NotBeNull(
            "Content-Disposition should be present for download=true");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment",
            "Content-Disposition should be 'attachment'");

        // The name in Content-Disposition should preserve the original (case-preserved) filename
        // Note: Path.GetFileName extracts the filename from the path
        var expectedName = Path.GetFileName(fileName);
        var dispositionFileName = response.Content.Headers.ContentDisposition.FileName;
        dispositionFileName.Should().NotBeNullOrEmpty(
            "Content-Disposition should include filename for emoji file");
    }

    [Fact]
    public async Task Download_UnicodeFilename_ContentDispositionPreservesName()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "donn\u00E9es.csv";
        await UploadFileAsync(client, bucketId, fileName, "data");

        var encodedPath = Uri.EscapeDataString(fileName);
        var response = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{encodedPath}/content?download=true", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Download with ?download=true should work for unicode filename");

        response.Content.Headers.ContentDisposition.Should().NotBeNull(
            "Content-Disposition should be present for download=true");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    // ════════════════════════════════════════════════════════════════════
    // ── File Listing with Special Filenames ────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileList_AllSpecialCharFiles_ReturnCorrectPaths()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileNames = new[]
        {
            "\U0001f389party.txt",
            "donn\u00E9es.csv",
            "\u00FCber-file.txt",
            "my file.txt",
            "file+plus.txt",
            "file.name.with.dots.txt",
            ".hidden",
            "no-extension",
        };

        foreach (var name in fileNames)
        {
            await UploadFileAsync(client, bucketId, name, $"content-{name}");
        }

        var response = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files?limit=50", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(fileNames.Length,
            "All files with special characters should appear in the listing");

        var listedPaths = new HashSet<string>();
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            listedPaths.Add(items[i].GetProperty("path").GetString()!);
        }

        foreach (var name in fileNames)
        {
            var expectedPath = name;
            listedPaths.Should().Contain(expectedPath,
                $"File listing should contain '{expectedPath}'");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // ── HEAD Request with Special Filenames ────────────────────────────
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HeadRequest_EmojiFilename_ReturnsHeaders()
    {
        using var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var fileName = "\U0001f389party.txt";
        var fileContent = "Head request emoji content";
        await UploadFileAsync(client, bucketId, fileName, fileContent);

        var encodedPath = Uri.EscapeDataString(fileName);
        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/api/buckets/{bucketId}/files/{encodedPath}/content");
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "HEAD request should work for emoji filename");
        response.Content.Headers.ContentLength.Should().Be(
            Encoding.UTF8.GetByteCount(fileContent),
            "Content-Length should match file size for emoji filename");
    }
}
