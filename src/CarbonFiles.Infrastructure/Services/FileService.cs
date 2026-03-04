using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class FileService : IFileService
{
    private readonly IDbConnection _db;
    private readonly FileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<FileService> _logger;

    public FileService(IDbConnection db, FileStorageService storage, INotificationService notifications, ICacheService cache, ILogger<FileService> logger)
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

        var total = await Db.ExecuteScalarAsync<int>(_db,
            "SELECT COUNT(*) FROM Files WHERE BucketId = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId));

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

        var sql = $"SELECT * FROM Files WHERE BucketId = @bucketId ORDER BY {sortColumn} {sortDir} LIMIT @Limit OFFSET @Offset";
        var entities = await Db.QueryAsync(_db, sql,
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@Limit", pagination.Limit);
                p.AddWithValue("@Offset", pagination.Offset);
            },
            FileEntity.Read);
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

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            },
            FileEntity.Read);
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
        // Check bucket ownership
        var bucket = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @bucketId",
            p => p.AddWithValue("@bucketId", bucketId),
            BucketEntity.Read);
        if (bucket == null)
            return false;

        if (!auth.CanManage(bucket.Owner))
        {
            _logger.LogWarning("Access denied: delete file {Path} in bucket {BucketId}", path, bucketId);
            return false;
        }

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            },
            FileEntity.Read);
        if (entity == null)
            return false;

        // Use a transaction for multi-table delete
        using var tx = _db.BeginTransaction();

        // Delete associated short URL
        if (entity.ShortCode != null)
            await Db.ExecuteAsync(_db, "DELETE FROM ShortUrls WHERE Code = @ShortCode",
                p => p.AddWithValue("@ShortCode", entity.ShortCode), tx);

        // Update bucket stats
        await Db.ExecuteAsync(_db,
            "UPDATE Buckets SET FileCount = MAX(0, FileCount - 1), TotalSize = MAX(0, TotalSize - @Size) WHERE Id = @bucketId",
            p =>
            {
                p.AddWithValue("@Size", entity.Size);
                p.AddWithValue("@bucketId", bucketId);
            }, tx);

        await Db.ExecuteAsync(_db,
            "DELETE FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            }, tx);

        tx.Commit();

        // Delete from disk
        _storage.DeleteFile(bucketId, path);

        _logger.LogInformation("Deleted file {Path} from bucket {BucketId}", path, bucketId);

        await _notifications.NotifyFileDeleted(bucketId, path);
        _cache.InvalidateFile(bucketId, path);
        _cache.InvalidateBucket(bucketId);
        _cache.InvalidateStats();
        return true;
    }

    public async Task UpdateLastUsedAsync(string bucketId)
    {
        await Db.ExecuteAsync(_db,
            "UPDATE Buckets SET LastUsedAt = @now WHERE Id = @bucketId",
            p =>
            {
                p.AddWithValue("@now", DateTime.UtcNow);
                p.AddWithValue("@bucketId", bucketId);
            });
    }

    public async Task<bool> UpdateFileSizeAsync(string bucketId, string path, long newSize)
    {
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            },
            FileEntity.Read);
        if (entity == null)
            return false;

        var oldSize = entity.Size;
        var now = DateTime.UtcNow;

        using var tx = _db.BeginTransaction();

        await Db.ExecuteAsync(_db,
            "UPDATE Files SET Size = @newSize, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@newSize", newSize);
                p.AddWithValue("@now", now);
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            }, tx);

        // Update bucket total size
        await Db.ExecuteAsync(_db,
            "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize + @newSize) WHERE Id = @bucketId",
            p =>
            {
                p.AddWithValue("@oldSize", oldSize);
                p.AddWithValue("@newSize", newSize);
                p.AddWithValue("@bucketId", bucketId);
            }, tx);

        tx.Commit();

        _cache.InvalidateFile(bucketId, path);
        _cache.InvalidateBucket(bucketId);
        _cache.InvalidateStats();

        _logger.LogDebug("Updated file size for {Path} in bucket {BucketId}: {OldSize} -> {NewSize}", path, bucketId, oldSize, newSize);

        return true;
    }
}
