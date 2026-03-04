using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ContentObjectEntity
{
    public required string Hash { get; set; }
    public long Size { get; set; }
    public required string DiskPath { get; set; }
    public int RefCount { get; set; }
    public DateTime CreatedAt { get; set; }

    internal static ContentObjectEntity Read(SqliteDataReader r) => new()
    {
        Hash = r.GetString(r.GetOrdinal("Hash")),
        Size = r.GetInt64(r.GetOrdinal("Size")),
        DiskPath = r.GetString(r.GetOrdinal("DiskPath")),
        RefCount = r.GetInt32(r.GetOrdinal("RefCount")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}
