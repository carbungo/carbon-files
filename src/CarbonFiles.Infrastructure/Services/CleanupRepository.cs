using System.Data;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CleanupRepository
{
    private readonly IDbConnection _db;

    public CleanupRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<BucketEntity>> GetExpiredBucketsAsync(DateTime now, CancellationToken ct)
    {
        return await Db.QueryAsync(_db,
            "SELECT * FROM Buckets WHERE ExpiresAt IS NOT NULL AND ExpiresAt < @now",
            p => p.AddWithValue("@now", now),
            BucketEntity.Read,
            ct: ct);
    }

    public async Task<List<ContentObjectEntity>> GetOrphanedContentAsync(DateTime olderThan, CancellationToken ct)
    {
        return await Db.QueryAsync(_db,
            "SELECT * FROM ContentObjects WHERE RefCount <= 0 AND CreatedAt < @olderThan",
            p => p.AddWithValue("@olderThan", olderThan),
            ContentObjectEntity.Read, ct: ct);
    }

    public async Task DeleteContentObjectAsync(string hash, CancellationToken ct)
    {
        await Db.ExecuteAsync(_db,
            "DELETE FROM ContentObjects WHERE Hash = @hash AND RefCount <= 0",
            p => p.AddWithValue("@hash", hash), ct: ct);
    }

    public async Task DeleteBucketAndRelatedAsync(string bucketId, CancellationToken ct)
    {
        using var tx = _db.BeginTransaction();

        // Decrement content ref counts for all CAS files in this bucket
        await Db.ExecuteAsync(_db,
            "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash IN (SELECT ContentHash FROM Files WHERE BucketId = @bucketId AND ContentHash IS NOT NULL)",
            p => p.AddWithValue("@bucketId", bucketId), tx, ct);

        await Db.ExecuteAsync(_db,
            "DELETE FROM Files WHERE BucketId = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId), tx, ct);
        await Db.ExecuteAsync(_db,
            "DELETE FROM ShortUrls WHERE BucketId = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId), tx, ct);
        await Db.ExecuteAsync(_db,
            "DELETE FROM UploadTokens WHERE BucketId = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId), tx, ct);
        await Db.ExecuteAsync(_db,
            "DELETE FROM Buckets WHERE Id = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId), tx, ct);

        tx.Commit();
    }
}
