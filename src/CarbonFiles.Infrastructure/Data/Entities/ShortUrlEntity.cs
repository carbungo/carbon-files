using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ShortUrlEntity
{
    public required string Code { get; set; }  // PK, 6-char
    public required string BucketId { get; set; }
    public required string FilePath { get; set; }
    public DateTime CreatedAt { get; set; }

    internal static ShortUrlEntity Read(SqliteDataReader r) => new()
    {
        Code = r.GetString(r.GetOrdinal("Code")),
        BucketId = r.GetString(r.GetOrdinal("BucketId")),
        FilePath = r.GetString(r.GetOrdinal("FilePath")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}
