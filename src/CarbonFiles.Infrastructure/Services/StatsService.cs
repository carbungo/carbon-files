using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Services;

public sealed class StatsService : IStatsService
{
    private readonly CarbonFilesDbContext _db;

    public StatsService(CarbonFilesDbContext db)
    {
        _db = db;
    }

    public async Task<StatsResponse> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var totalBuckets = await _db.Buckets
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > now)
            .CountAsync(ct);

        var totalFiles = await _db.Files.CountAsync(ct);

        var totalSize = await _db.Files.SumAsync(f => (long?)f.Size, ct) ?? 0;

        var totalKeys = await _db.ApiKeys.CountAsync(ct);

        var totalDownloads = await _db.Buckets
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > now)
            .SumAsync(b => (long?)b.DownloadCount, ct) ?? 0;

        var storageByOwner = await _db.Buckets
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > now)
            .GroupBy(b => b.Owner)
            .Select(g => new OwnerStats
            {
                Owner = g.Key,
                BucketCount = g.Count(),
                FileCount = g.Sum(b => b.FileCount),
                TotalSize = g.Sum(b => b.TotalSize)
            }).ToListAsync(ct);

        return new StatsResponse
        {
            TotalBuckets = totalBuckets,
            TotalFiles = totalFiles,
            TotalSize = totalSize,
            TotalKeys = totalKeys,
            TotalDownloads = totalDownloads,
            StorageByOwner = storageByOwner
        };
    }
}
