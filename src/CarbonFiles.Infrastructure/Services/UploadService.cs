using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class UploadService : IUploadService
{
    private readonly IDbConnection _db;
    private readonly FileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<UploadService> _logger;

    public UploadService(IDbConnection db, FileStorageService storage, INotificationService notifications, ICacheService cache, ILogger<UploadService> logger)
    {
        _db = db;
        _storage = storage;
        _notifications = notifications;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BucketFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default)
    {
        _logger.LogDebug("Storing file {Path} in bucket {BucketId}", path, bucketId);

        var normalized = path.ToLowerInvariant();
        var name = Path.GetFileName(path);
        var mimeType = MimeDetector.DetectFromExtension(path);

        // Stream content to disk (pipelined: network reads and disk writes run concurrently)
        var size = await _storage.StoreAsync(bucketId, normalized, content, maxSize, ct);

        // Check if file already exists
        var existing = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @normalized",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@normalized", normalized);
            },
            FileEntity.Read);
        var now = DateTime.UtcNow;

        if (existing != null)
        {
            // Update existing file
            var oldSize = existing.Size;

            using var tx = _db.BeginTransaction();

            await Db.ExecuteAsync(_db,
                "UPDATE Files SET Size = @size, MimeType = @mimeType, Name = @name, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @normalized",
                p =>
                {
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@mimeType", mimeType);
                    p.AddWithValue("@name", name);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                    p.AddWithValue("@normalized", normalized);
                }, tx);

            // Update bucket total size (difference) and last used
            await Db.ExecuteAsync(_db,
                "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize) + @size, LastUsedAt = @now WHERE Id = @bucketId",
                p =>
                {
                    p.AddWithValue("@oldSize", oldSize);
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Updated file {Path} in bucket {BucketId} ({OldSize} -> {Size} bytes)", normalized, bucketId, oldSize, size);

            var updatedFile = ToBucketFile(existing.Path, name, size, mimeType, existing.ShortCode, existing.CreatedAt, now);
            await _notifications.NotifyFileUpdated(bucketId, updatedFile);
            return updatedFile;
        }
        else
        {
            // Create new file
            var shortCode = IdGenerator.GenerateShortCode();

            using var tx = _db.BeginTransaction();

            await Db.ExecuteAsync(_db,
                "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, ShortCode, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @ShortCode, @CreatedAt, @UpdatedAt)",
                p =>
                {
                    p.AddWithValue("@BucketId", bucketId);
                    p.AddWithValue("@Path", normalized);
                    p.AddWithValue("@Name", name);
                    p.AddWithValue("@Size", size);
                    p.AddWithValue("@MimeType", mimeType);
                    p.AddWithValue("@ShortCode", shortCode);
                    p.AddWithValue("@CreatedAt", now);
                    p.AddWithValue("@UpdatedAt", now);
                }, tx);

            // Create short URL
            await Db.ExecuteAsync(_db,
                "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
                p =>
                {
                    p.AddWithValue("@Code", shortCode);
                    p.AddWithValue("@BucketId", bucketId);
                    p.AddWithValue("@FilePath", normalized);
                    p.AddWithValue("@CreatedAt", now);
                }, tx);

            // Update bucket stats
            await Db.ExecuteAsync(_db,
                "UPDATE Buckets SET FileCount = FileCount + 1, TotalSize = TotalSize + @size, LastUsedAt = @now WHERE Id = @bucketId",
                p =>
                {
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Created file {Path} in bucket {BucketId} ({Size} bytes, short code {ShortCode})", normalized, bucketId, size, shortCode);

            var createdFile = ToBucketFile(normalized, name, size, mimeType, shortCode, now, now);
            await _notifications.NotifyFileCreated(bucketId, createdFile);
            return createdFile;
        }
    }

    public Task<long> GetStoredFileSizeAsync(string bucketId, string path)
    {
        var size = _storage.GetFileSize(bucketId, path.ToLowerInvariant());
        return Task.FromResult(size);
    }

    private static BucketFile ToBucketFile(string path, string name, long size, string mimeType, string? shortCode, DateTime createdAt, DateTime updatedAt) => new()
    {
        Path = path,
        Name = name,
        Size = size,
        MimeType = mimeType,
        ShortCode = shortCode,
        ShortUrl = shortCode != null ? $"/s/{shortCode}" : null,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };
}
