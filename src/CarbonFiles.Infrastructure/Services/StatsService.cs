using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Services;

public sealed class StatsService : IStatsService
{
    private readonly IDbConnection _db;

    public StatsService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<StatsResponse> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var totalBuckets = await Db.ExecuteScalarAsync<int>(_db,
            "SELECT COUNT(*) FROM Buckets WHERE ExpiresAt IS NULL OR ExpiresAt > @now",
            p => p.AddWithValue("@now", now));

        var totalFiles = await Db.ExecuteScalarAsync<int>(_db,
            "SELECT COUNT(*) FROM Files");

        var totalSize = await Db.ExecuteScalarAsync<long>(_db,
            "SELECT COALESCE(SUM(Size), 0) FROM Files");

        var totalKeys = await Db.ExecuteScalarAsync<int>(_db,
            "SELECT COUNT(*) FROM ApiKeys");

        var totalDownloads = await Db.ExecuteScalarAsync<long>(_db,
            "SELECT COALESCE(SUM(DownloadCount), 0) FROM Buckets WHERE ExpiresAt IS NULL OR ExpiresAt > @now",
            p => p.AddWithValue("@now", now));

        var storageByOwner = await Db.QueryAsync(_db,
            """
            SELECT Owner, COUNT(*) AS BucketCount, SUM(FileCount) AS FileCount, SUM(TotalSize) AS TotalSize
            FROM Buckets
            WHERE ExpiresAt IS NULL OR ExpiresAt > @now
            GROUP BY Owner
            """,
            p => p.AddWithValue("@now", now),
            r => new OwnerStats
            {
                Owner = r.GetString(r.GetOrdinal("Owner")),
                BucketCount = r.GetInt32(r.GetOrdinal("BucketCount")),
                FileCount = r.GetInt32(r.GetOrdinal("FileCount")),
                TotalSize = r.GetInt64(r.GetOrdinal("TotalSize")),
            });

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
