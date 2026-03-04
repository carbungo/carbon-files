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

    public async Task DeleteBucketAndRelatedAsync(string bucketId, CancellationToken ct)
    {
        using var tx = _db.BeginTransaction();

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
