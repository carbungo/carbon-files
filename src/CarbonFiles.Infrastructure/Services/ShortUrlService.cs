using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ShortUrlService : IShortUrlService
{
    private readonly IDbConnection _db;
    private readonly ICacheService _cache;
    private readonly ILogger<ShortUrlService> _logger;

    public ShortUrlService(IDbConnection db, ICacheService cache, ILogger<ShortUrlService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> CreateAsync(string bucketId, string filePath)
    {
        const int maxAttempts = 5;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = IdGenerator.GenerateShortCode();
            var now = DateTime.UtcNow;

            try
            {
                await Db.ExecuteAsync(_db,
                    "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@code, @bucketId, @filePath, @now)",
                    p =>
                    {
                        p.AddWithValue("@code", code);
                        p.AddWithValue("@bucketId", bucketId);
                        p.AddWithValue("@filePath", filePath);
                        p.AddWithValue("@now", now);
                    });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (unique violation)
            {
                _logger.LogDebug("Short code collision, retrying (attempt {Attempt})", attempt + 1);
                continue;
            }

            _cache.SetShortUrl(code, bucketId, filePath);
            _logger.LogInformation("Created short URL {Code} for bucket {BucketId} file {FilePath}", code, bucketId, filePath);

            return code;
        }

        throw new InvalidOperationException("Failed to generate a unique short code after maximum attempts.");
    }

    public async Task<string?> ResolveAsync(string code)
    {
        var cached = _cache.GetShortUrl(code);
        if (cached != null)
            return $"/api/buckets/{cached.Value.BucketId}/files/{Uri.EscapeDataString(cached.Value.FilePath)}/content";

        var shortUrl = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code",
            p => p.AddWithValue("@code", code),
            ShortUrlEntity.Read);
        if (shortUrl == null)
        {
            _logger.LogDebug("Short URL {Code} not found", code);
            return null;
        }

        // Verify the associated bucket hasn't expired
        var bucket = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @Id",
            p => p.AddWithValue("@Id", shortUrl.BucketId),
            BucketEntity.Read);
        if (bucket == null)
            return null;

        if (bucket.IsExpired)
        {
            _logger.LogDebug("Short URL {Code} points to expired bucket {BucketId}", code, shortUrl.BucketId);
            return null;
        }

        _cache.SetShortUrl(code, shortUrl.BucketId, shortUrl.FilePath);
        return $"/api/buckets/{shortUrl.BucketId}/files/{Uri.EscapeDataString(shortUrl.FilePath)}/content";
    }

    public async Task<bool> DeleteAsync(string code, AuthContext auth)
    {
        var shortUrl = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code",
            p => p.AddWithValue("@code", code),
            ShortUrlEntity.Read);
        if (shortUrl == null)
            return false;

        // Find the bucket owner and verify auth can manage
        var bucket = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @Id",
            p => p.AddWithValue("@Id", shortUrl.BucketId),
            BucketEntity.Read);
        if (bucket == null)
            return false;

        if (!auth.CanManage(bucket.Owner))
            return false;

        await Db.ExecuteAsync(_db, "DELETE FROM ShortUrls WHERE Code = @code",
            p => p.AddWithValue("@code", code));

        _logger.LogInformation("Deleted short URL {Code}", code);

        _cache.InvalidateShortUrl(code);
        return true;
    }
}
