using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class FileEntity
{
    public required string BucketId { get; set; }
    public required string Path { get; set; }  // Composite PK with BucketId
    public required string Name { get; set; }
    public long Size { get; set; }
    public required string MimeType { get; set; }
    public string? ShortCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    internal static FileEntity Read(SqliteDataReader r) => new()
    {
        BucketId = r.GetString(r.GetOrdinal("BucketId")),
        Path = r.GetString(r.GetOrdinal("Path")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Size = r.GetInt64(r.GetOrdinal("Size")),
        MimeType = r.GetString(r.GetOrdinal("MimeType")),
        ShortCode = r.IsDBNull(r.GetOrdinal("ShortCode")) ? null : r.GetString(r.GetOrdinal("ShortCode")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
    };
}
