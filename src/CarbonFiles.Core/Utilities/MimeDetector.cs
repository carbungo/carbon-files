using System.Collections.Frozen;

namespace CarbonFiles.Core.Utilities;

public static class MimeDetector
{
    private static readonly FrozenDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon", [".bmp"] = "image/bmp", [".tiff"] = "image/tiff",
        [".avif"] = "image/avif",
        [".heic"] = "image/heic", [".heif"] = "image/heif",
        // Video
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime", [".mkv"] = "video/x-matroska", [".wmv"] = "video/x-ms-wmv",
        // Audio
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg",
        [".flac"] = "audio/flac", [".aac"] = "audio/aac", [".m4a"] = "audio/mp4",
        // Documents
        [".pdf"] = "application/pdf", [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        // Archives
        [".zip"] = "application/zip", [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip", [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        // Code/text
        [".json"] = "application/json", [".map"] = "application/json",
        [".webmanifest"] = "application/manifest+json", [".xml"] = "application/xml",
        [".rss"] = "application/rss+xml", [".atom"] = "application/atom+xml",
        [".js"] = "text/javascript", [".mjs"] = "text/javascript",
        [".ts"] = "text/typescript", [".tsx"] = "text/typescript",
        [".jsx"] = "text/javascript",
        [".html"] = "text/html", [".htm"] = "text/html",
        [".xhtml"] = "application/xhtml+xml",
        [".css"] = "text/css", [".csv"] = "text/csv",
        [".md"] = "text/markdown", [".txt"] = "text/plain",
        [".yaml"] = "text/yaml", [".yml"] = "text/yaml",
        [".toml"] = "text/toml",
        [".rs"] = "text/x-rust", [".cs"] = "text/x-csharp",
        [".py"] = "text/x-python", [".rb"] = "text/x-ruby",
        [".go"] = "text/x-go", [".java"] = "text/x-java",
        [".c"] = "text/x-c", [".cpp"] = "text/x-c++",
        [".h"] = "text/x-c", [".hpp"] = "text/x-c++",
        [".swift"] = "text/x-swift", [".kt"] = "text/x-kotlin",
        [".sh"] = "text/x-shellscript", [".bash"] = "text/x-shellscript",
        [".sql"] = "text/x-sql",
        [".dockerfile"] = "text/x-dockerfile",
        // Fonts
        [".woff"] = "font/woff", [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf", [".otf"] = "font/otf",
        // Data
        [".wasm"] = "application/wasm",
        [".bin"] = "application/octet-stream",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string DetectFromExtension(string filename)
    {
        var ext = Path.GetExtension(filename);
        if (string.IsNullOrEmpty(ext))
            return "application/octet-stream";
        return MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
    }

    public static string Detect(string filename, ReadOnlySpan<byte> prefix)
    {
        var known = DetectFromExtension(filename);
        if (known != "application/octet-stream") return known;

        if (prefix.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (prefix.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8)) return "image/gif";
        if (prefix.StartsWith("%PDF-"u8)) return "application/pdf";
        if (prefix.StartsWith("\0asm"u8)) return "application/wasm";
        if (prefix.StartsWith("wOFF"u8)) return "font/woff";
        if (prefix.StartsWith("wOF2"u8)) return "font/woff2";
        if (prefix.StartsWith("PK\x03\x04"u8)) return "application/zip";
        if (prefix.StartsWith(new byte[] { 0x1f, 0x8b })) return "application/gzip";
        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (prefix.Length >= 12 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = prefix.Slice(8, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8)) return "image/avif";
            if (brand.SequenceEqual("isom"u8) || brand.SequenceEqual("mp41"u8) || brand.SequenceEqual("mp42"u8)) return "video/mp4";
        }
        if (prefix.IsEmpty) return "application/octet-stream";

        // Strict UTF-8 avoids declaring arbitrary binary data as text.
        try
        {
            var text = new System.Text.UTF8Encoding(false, true).GetString(prefix);
            if (text.IndexOf('\0') >= 0 || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                return "application/octet-stream";
            var trimmed = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "text/html";
            if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return "application/xml";
            if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) &&
                (trimmed.EndsWith('}') || trimmed.EndsWith(']'))) return "application/json";
            return "text/plain";
        }
        catch (System.Text.DecoderFallbackException)
        {
            return "application/octet-stream";
        }
    }

    public static string WithCharset(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mimeType is "application/json" or "application/manifest+json" or "application/xml" or
            "application/xhtml+xml" or "application/rss+xml" or "application/atom+xml" or
            "application/javascript" or "image/svg+xml"
            ? mimeType + "; charset=utf-8" : mimeType;
}
