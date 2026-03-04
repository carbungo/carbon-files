using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class UploadTokenEntity
{
    public required string Token { get; set; }  // PK, "cfu_..."
    public required string BucketId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int? MaxUploads { get; set; }
    public int UploadsUsed { get; set; }
    public DateTime CreatedAt { get; set; }

    internal static UploadTokenEntity Read(SqliteDataReader r) => new()
    {
        Token = r.GetString(r.GetOrdinal("Token")),
        BucketId = r.GetString(r.GetOrdinal("BucketId")),
        ExpiresAt = r.GetDateTime(r.GetOrdinal("ExpiresAt")),
        MaxUploads = r.IsDBNull(r.GetOrdinal("MaxUploads")) ? null : r.GetInt32(r.GetOrdinal("MaxUploads")),
        UploadsUsed = r.GetInt32(r.GetOrdinal("UploadsUsed")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}
