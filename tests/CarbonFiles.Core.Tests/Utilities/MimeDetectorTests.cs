using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Utilities;

public class MimeDetectorTests
{
    [Theory]
    [InlineData("image.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("icon.svg", "image/svg+xml")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("data.json", "application/json")]
    [InlineData("app.js", "text/javascript")]
    [InlineData("index.ts", "text/typescript")]
    [InlineData("page.html", "text/html")]
    [InlineData("style.css", "text/css")]
    [InlineData("readme.md", "text/markdown")]
    [InlineData("src/main.rs", "text/x-rust")]
    [InlineData("Program.cs", "text/x-csharp")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("app.wasm", "application/wasm")]
    [InlineData("site.webmanifest", "application/manifest+json")]
    [InlineData("bundle.js.map", "application/json")]
    [InlineData("module.mjs", "text/javascript")]
    [InlineData("image.avif", "image/avif")]
    [InlineData("icon.ico", "image/x-icon")]
    [InlineData("font.ttf", "font/ttf")]
    [InlineData("font.otf", "font/otf")]
    [InlineData("data.xml", "application/xml")]
    [InlineData("data.csv", "text/csv")]
    [InlineData("movie.webm", "video/webm")]
    public void DetectFromExtension_ReturnsCorrectMimeType(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }

    [Theory]
    [InlineData("file.xyz")]
    [InlineData("file.unknown")]
    public void DetectFromExtension_ReturnsOctetStreamForUnknownExtension(string filename)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("Makefile")]
    [InlineData("LICENSE")]
    [InlineData("Dockerfile")]
    public void DetectFromExtension_ReturnsOctetStreamForNoExtension(string filename)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("src/components/App.tsx", "text/typescript")]
    [InlineData("assets/images/logo.webp", "image/webp")]
    [InlineData("dist/bundle.js", "text/javascript")]
    public void DetectFromExtension_HandlesPathsWithDirectories(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }

    [Theory]
    [InlineData("IMAGE.PNG", "image/png")]
    [InlineData("Photo.JPG", "image/jpeg")]
    [InlineData("Data.JSON", "application/json")]
    public void DetectFromExtension_IsCaseInsensitive(string filename, string expectedMime)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expectedMime);
    }

    [Theory]
    [InlineData("{\"ok\":true}", "application/json")]
    [InlineData("<!doctype html><html></html>", "text/html")]
    [InlineData("plain text\n", "text/plain")]
    public void Detect_ExtensionlessText_UsesContent(string content, string expectedMime)
    {
        MimeDetector.Detect("LICENSE", System.Text.Encoding.UTF8.GetBytes(content)).Should().Be(expectedMime);
    }

    [Fact]
    public void Detect_ExtensionlessBinary_UsesMagicOrOctetStream()
    {
        MimeDetector.Detect("image", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).Should().Be("image/png");
        MimeDetector.Detect("blob", [0x00, 0xff, 0x10]).Should().Be("application/octet-stream");
    }

    [Fact]
    public void WithCharset_TextualMimeTypes_IncludeUtf8()
    {
        MimeDetector.WithCharset("application/manifest+json").Should().Be("application/manifest+json; charset=utf-8");
        MimeDetector.WithCharset("image/svg+xml").Should().Be("image/svg+xml; charset=utf-8");
        MimeDetector.WithCharset("image/png").Should().Be("image/png");
    }
}
