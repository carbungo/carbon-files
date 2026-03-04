using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class BucketEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Owner { get; set; }
    public string? OwnerKeyPrefix { get; set; }  // FK to ApiKeyEntity
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public long DownloadCount { get; set; }

    internal bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;

    internal static BucketEntity Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("Id")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Owner = r.GetString(r.GetOrdinal("Owner")),
        OwnerKeyPrefix = r.IsDBNull(r.GetOrdinal("OwnerKeyPrefix")) ? null : r.GetString(r.GetOrdinal("OwnerKeyPrefix")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        ExpiresAt = r.IsDBNull(r.GetOrdinal("ExpiresAt")) ? null : r.GetDateTime(r.GetOrdinal("ExpiresAt")),
        LastUsedAt = r.IsDBNull(r.GetOrdinal("LastUsedAt")) ? null : r.GetDateTime(r.GetOrdinal("LastUsedAt")),
        FileCount = r.GetInt32(r.GetOrdinal("FileCount")),
        TotalSize = r.GetInt64(r.GetOrdinal("TotalSize")),
        DownloadCount = r.GetInt64(r.GetOrdinal("DownloadCount")),
    };
}
