using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CleanupRepository
{
    private readonly CarbonFilesDbContext _db;

    public CleanupRepository(CarbonFilesDbContext db)
    {
        _db = db;
    }

    public async Task<List<BucketEntity>> GetExpiredBucketsAsync(DateTime now, CancellationToken ct)
    {
        return await _db.Buckets
            .Where(b => b.ExpiresAt != null && b.ExpiresAt < now)
            .ToListAsync(ct);
    }

    public async Task DeleteFilesForBucketAsync(string bucketId, CancellationToken ct)
    {
        await _db.Files.Where(f => f.BucketId == bucketId).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteShortUrlsForBucketAsync(string bucketId, CancellationToken ct)
    {
        await _db.ShortUrls.Where(s => s.BucketId == bucketId).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteUploadTokensForBucketAsync(string bucketId, CancellationToken ct)
    {
        await _db.UploadTokens.Where(t => t.BucketId == bucketId).ExecuteDeleteAsync(ct);
    }

    public void RemoveBucket(BucketEntity bucket)
    {
        _db.Buckets.Remove(bucket);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}
