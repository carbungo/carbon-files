using System.Collections.Concurrent;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    // Track cache keys per bucket for bulk invalidation
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _bucketKeys = new();

    // TTLs (safety nets — eager invalidation is the primary mechanism)
    private static readonly TimeSpan BucketTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortUrlTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan UploadTokenTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(5);

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // --- Bucket ---

    public BucketDetailResponse? GetBucket(string id)
    {
        var key = $"bucket:{id}";
        if (_cache.TryGetValue(key, out BucketDetailResponse? bucket))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return bucket;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetBucket(string id, BucketDetailResponse bucket)
    {
        var key = $"bucket:{id}";
        _cache.Set(key, bucket, BucketTtl);
        TrackKey(id, key);
    }

    public void InvalidateBucket(string id)
    {
        var key = $"bucket:{id}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    // --- File Metadata ---

    public BucketFile? GetFileMetadata(string bucketId, string path)
    {
        var key = $"file:{bucketId}:{path}";
        if (_cache.TryGetValue(key, out BucketFile? file))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return file;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetFileMetadata(string bucketId, string path, BucketFile file)
    {
        var key = $"file:{bucketId}:{path}";
        _cache.Set(key, file, FileTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateFile(string bucketId, string path)
    {
        var key = $"file:{bucketId}:{path}";
        _cache.Remove(key);
        UntrackKey(bucketId, key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateFilesForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "file:");
    }

    // --- Short URL ---

    public (string BucketId, string FilePath)? GetShortUrl(string code)
    {
        var key = $"short:{code}";
        if (_cache.TryGetValue(key, out (string BucketId, string FilePath) entry))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return entry;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetShortUrl(string code, string bucketId, string filePath)
    {
        var key = $"short:{code}";
        _cache.Set(key, (bucketId, filePath), ShortUrlTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateShortUrl(string code)
    {
        var key = $"short:{code}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateShortUrlsForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "short:");
    }

    // --- Upload Token ---

    public (string BucketId, bool IsValid)? GetUploadToken(string token)
    {
        var key = $"uploadtoken:{token}";
        if (_cache.TryGetValue(key, out (string BucketId, bool IsValid) entry))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return entry;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetUploadToken(string token, string bucketId, bool isValid)
    {
        var key = $"uploadtoken:{token}";
        _cache.Set(key, (bucketId, isValid), UploadTokenTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateUploadToken(string token)
    {
        var key = $"uploadtoken:{token}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateUploadTokensForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "uploadtoken:");
    }

    // --- Stats ---

    public StatsResponse? GetStats()
    {
        var key = "stats";
        if (_cache.TryGetValue(key, out StatsResponse? stats))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return stats;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetStats(StatsResponse stats)
    {
        _cache.Set("stats", stats, StatsTtl);
    }

    public void InvalidateStats()
    {
        _cache.Remove("stats");
        _logger.LogDebug("Cache invalidated: stats");
    }

    // --- Key Tracking ---

    private void TrackKey(string bucketId, string cacheKey)
    {
        var keys = _bucketKeys.GetOrAdd(bucketId, _ => new ConcurrentDictionary<string, byte>());
        keys.TryAdd(cacheKey, 0);
    }

    private void UntrackKey(string bucketId, string cacheKey)
    {
        if (_bucketKeys.TryGetValue(bucketId, out var keys))
            keys.TryRemove(cacheKey, out _);
    }

    private void InvalidateAllForBucket(string bucketId, string prefix)
    {
        if (!_bucketKeys.TryGetValue(bucketId, out var keys))
            return;

        var toRemove = new List<string>();
        foreach (var key in keys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _cache.Remove(key);
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove)
            keys.TryRemove(key, out _);

        if (keys.IsEmpty)
            _bucketKeys.TryRemove(bucketId, out _);

        if (toRemove.Count > 0)
            _logger.LogDebug("Bulk invalidated {Count} {Prefix}* keys for bucket {BucketId}", toRemove.Count, prefix, bucketId);
    }
}
