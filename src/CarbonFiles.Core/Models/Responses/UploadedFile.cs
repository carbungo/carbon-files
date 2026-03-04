namespace CarbonFiles.Core.Models.Responses;

public sealed class UploadedFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long Size { get; set; }
    public required string MimeType { get; init; }
    public string? ShortCode { get; set; }
    public string? ShortUrl { get; set; }
    public string? Sha256 { get; set; }
    public bool Deduplicated { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    public BucketFile ToBucketFile() => new()
    {
        Path = Path, Name = Name, Size = Size, MimeType = MimeType,
        ShortCode = ShortCode, ShortUrl = ShortUrl, Sha256 = Sha256,
        CreatedAt = CreatedAt, UpdatedAt = UpdatedAt
    };
}
