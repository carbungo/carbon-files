using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class FileService : IFileService
{
    private readonly CarbonFilesDbContext _db;
    private readonly FileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<FileService> _logger;

    public FileService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ICacheService cache, ILogger<FileService> logger)
    {
        _db = db;
        _storage = storage;
        _notifications = notifications;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PaginatedResponse<BucketFile>> ListAsync(string bucketId, PaginationParams pagination)
    {
        _logger.LogDebug("Listing files in bucket {BucketId} (limit={Limit}, offset={Offset})", bucketId, pagination.Limit, pagination.Offset);

        // Use raw SQL to avoid dynamic IQueryable chains that the EF Core precompiler rejects
        var total = await _db.Files.Where(f => f.BucketId == bucketId).CountAsync();

        // Sort column mapping (whitelist to prevent SQL injection)
        var sortColumn = pagination.Sort?.ToLowerInvariant() switch
        {
            "name" => "Name",
            "path" => "Path",
            "size" => "Size",
            "mime_type" => "MimeType",
            "updated_at" => "UpdatedAt",
            "created_at" => "CreatedAt",
            _ => "CreatedAt"
        };
        var sortDir = pagination.Order?.ToLowerInvariant() == "asc" ? "ASC" : "DESC";

        var sql = $"SELECT * FROM Files WHERE BucketId = {{0}} ORDER BY {sortColumn} {sortDir} LIMIT {{1}} OFFSET {{2}}";
        var entities = await _db.Files.FromSqlRaw(sql, bucketId, pagination.Limit, pagination.Offset).ToListAsync();
        var items = entities.Select(f => f.ToBucketFile()).ToList();

        return new PaginatedResponse<BucketFile>
        {
            Items = items,
            Total = total,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    public async Task<BucketFile?> GetMetadataAsync(string bucketId, string path)
    {
        var cached = _cache.GetFileMetadata(bucketId, path);
        if (cached != null)
            return cached;

        var normalized = path.ToLowerInvariant();
        var entity = await _db.Files.FirstOrDefaultAsync(f => f.BucketId == bucketId && f.Path == normalized);
        if (entity == null)
        {
            _logger.LogDebug("File {Path} not found in bucket {BucketId}", path, bucketId);
            return null;
        }

        var file = entity.ToBucketFile();
        _cache.SetFileMetadata(bucketId, path, file);
        return file;
    }

    public async Task<bool> DeleteAsync(string bucketId, string path, AuthContext auth)
    {
        var normalized = path.ToLowerInvariant();

        // Check bucket ownership
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
        if (bucket == null)
            return false;

        if (!auth.CanManage(bucket.Owner))
        {
            _logger.LogWarning("Access denied: delete file {Path} in bucket {BucketId}", path, bucketId);
            return false;
        }

        var entity = await _db.Files.FirstOrDefaultAsync(f => f.BucketId == bucketId && f.Path == normalized);
        if (entity == null)
            return false;

        // Delete associated short URL
        if (entity.ShortCode != null)
        {
            var shortUrl = await _db.ShortUrls.FirstOrDefaultAsync(s => s.Code == entity.ShortCode);
            if (shortUrl != null)
                _db.ShortUrls.Remove(shortUrl);
        }

        // Update bucket stats
        bucket.FileCount = Math.Max(0, bucket.FileCount - 1);
        bucket.TotalSize = Math.Max(0, bucket.TotalSize - entity.Size);

        _db.Files.Remove(entity);
        await _db.SaveChangesAsync();

        // Delete from disk
        _storage.DeleteFile(bucketId, normalized);

        _logger.LogInformation("Deleted file {Path} from bucket {BucketId}", normalized, bucketId);

        await _notifications.NotifyFileDeleted(bucketId, normalized);
        _cache.InvalidateFile(bucketId, normalized);
        _cache.InvalidateBucket(bucketId);
        _cache.InvalidateStats();
        return true;
    }

    public async Task UpdateLastUsedAsync(string bucketId)
    {
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
        if (bucket != null)
        {
            bucket.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> UpdateFileSizeAsync(string bucketId, string path, long newSize)
    {
        var normalized = path.ToLowerInvariant();
        var entity = await _db.Files.FirstOrDefaultAsync(f => f.BucketId == bucketId && f.Path == normalized);
        if (entity == null)
            return false;

        var oldSize = entity.Size;
        entity.Size = newSize;
        entity.UpdatedAt = DateTime.UtcNow;

        // Update bucket total size
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
        if (bucket != null)
        {
            bucket.TotalSize = Math.Max(0, bucket.TotalSize - oldSize + newSize);
        }

        await _db.SaveChangesAsync();
        _cache.InvalidateFile(bucketId, path);
        _cache.InvalidateBucket(bucketId);
        _cache.InvalidateStats();

        _logger.LogDebug("Updated file size for {Path} in bucket {BucketId}: {OldSize} -> {NewSize}", path, bucketId, oldSize, newSize);

        return true;
    }
}
