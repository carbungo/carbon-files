using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
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
    private readonly ContentStorageService _contentStorage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<UploadService> _logger;

    public UploadService(IDbConnection db, FileStorageService storage, ContentStorageService contentStorage,
        INotificationService notifications, ICacheService cache, ILogger<UploadService> logger)
    {
        _db = db;
        _storage = storage;
        _contentStorage = contentStorage;
        _notifications = notifications;
        _cache = cache;
        _logger = logger;
    }

    public async Task<UploadedFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default)
    {
        _logger.LogDebug("Storing file {Path} in bucket {BucketId}", path, bucketId);

        var name = Path.GetFileName(path);
        var mimeType = MimeDetector.DetectFromExtension(path);

        // Stream to temp file, computing SHA256 inline
        var (tempPath, size, hash) = await _storage.StoreToTempAsync(content, maxSize, ct);

        bool deduplicated = false;

        try
        {
            // Check if content already exists
            var existingContent = await Db.QueryFirstOrDefaultAsync(_db,
                "SELECT * FROM ContentObjects WHERE Hash = @hash",
                p => p.AddWithValue("@hash", hash),
                ContentObjectEntity.Read);

            if (existingContent != null)
            {
                // Dedup: increment ref count, delete temp file
                deduplicated = true;
                File.Delete(tempPath);
            }
            else
            {
                // New content: move to CAS store
                var diskPath = ContentStorageService.ComputeDiskPath(hash);
                _contentStorage.MoveToContentStore(tempPath, diskPath);
            }

            // Check if file record already exists
            var existing = await Db.QueryFirstOrDefaultAsync(_db,
                "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
                p =>
                {
                    p.AddWithValue("@bucketId", bucketId);
                    p.AddWithValue("@path", path);
                },
                FileEntity.Read);

            var now = DateTime.UtcNow;

            if (existing != null)
            {
                var oldSize = existing.Size;
                var oldHash = existing.ContentHash;

                using var tx = _db.BeginTransaction();

                // Update file record
                await Db.ExecuteAsync(_db,
                    "UPDATE Files SET Size = @size, MimeType = @mimeType, Name = @name, ContentHash = @hash, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @path",
                    p =>
                    {
                        p.AddWithValue("@size", size);
                        p.AddWithValue("@mimeType", mimeType);
                        p.AddWithValue("@name", name);
                        p.AddWithValue("@hash", hash);
                        p.AddWithValue("@now", now);
                        p.AddWithValue("@bucketId", bucketId);
                        p.AddWithValue("@path", path);
                    }, tx);

                // Increment new content ref count (if dedup, it already exists)
                if (deduplicated)
                {
                    await Db.ExecuteAsync(_db,
                        "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                        p => p.AddWithValue("@hash", hash), tx);
                }
                else
                {
                    // Insert new content object
                    var diskPath = ContentStorageService.ComputeDiskPath(hash);
                    await Db.ExecuteAsync(_db,
                        "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                        p =>
                        {
                            p.AddWithValue("@hash", hash);
                            p.AddWithValue("@size", size);
                            p.AddWithValue("@diskPath", diskPath);
                            p.AddWithValue("@now", now);
                        }, tx);
                }

                // Decrement old content ref count (if hash changed)
                if (oldHash != null && oldHash != hash)
                {
                    await Db.ExecuteAsync(_db,
                        "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @oldHash",
                        p => p.AddWithValue("@oldHash", oldHash), tx);
                }

                // Update bucket total size
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

                _cache.InvalidateFile(bucketId, path);
                _cache.InvalidateBucket(bucketId);
                _cache.InvalidateStats();

                _logger.LogInformation("Updated file {Path} in bucket {BucketId} ({OldSize} -> {Size} bytes)", path, bucketId, oldSize, size);

                var updatedFile = ToUploadedFile(existing.Path, name, size, mimeType, existing.ShortCode, hash, deduplicated, existing.CreatedAt, now);
                await _notifications.NotifyFileUpdated(bucketId, updatedFile.ToBucketFile());
                return updatedFile;
            }
            else
            {
                // New file
                var shortCode = IdGenerator.GenerateShortCode();

                using var tx = _db.BeginTransaction();

                // Insert or increment content object
                if (deduplicated)
                {
                    await Db.ExecuteAsync(_db,
                        "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                        p => p.AddWithValue("@hash", hash), tx);
                }
                else
                {
                    var diskPath = ContentStorageService.ComputeDiskPath(hash);
                    await Db.ExecuteAsync(_db,
                        "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                        p =>
                        {
                            p.AddWithValue("@hash", hash);
                            p.AddWithValue("@size", size);
                            p.AddWithValue("@diskPath", diskPath);
                            p.AddWithValue("@now", now);
                        }, tx);
                }

                // Insert file record
                await Db.ExecuteAsync(_db,
                    "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, ShortCode, ContentHash, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @ShortCode, @ContentHash, @CreatedAt, @UpdatedAt)",
                    p =>
                    {
                        p.AddWithValue("@BucketId", bucketId);
                        p.AddWithValue("@Path", path);
                        p.AddWithValue("@Name", name);
                        p.AddWithValue("@Size", size);
                        p.AddWithValue("@MimeType", mimeType);
                        p.AddWithValue("@ShortCode", shortCode);
                        p.AddWithValue("@ContentHash", hash);
                        p.AddWithValue("@CreatedAt", now);
                        p.AddWithValue("@UpdatedAt", now);
                    }, tx);

                // Short URL
                await Db.ExecuteAsync(_db,
                    "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
                    p =>
                    {
                        p.AddWithValue("@Code", shortCode);
                        p.AddWithValue("@BucketId", bucketId);
                        p.AddWithValue("@FilePath", path);
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

                _cache.InvalidateFile(bucketId, path);
                _cache.InvalidateBucket(bucketId);
                _cache.InvalidateStats();

                _logger.LogInformation("Created file {Path} in bucket {BucketId} ({Size} bytes, short code {ShortCode})", path, bucketId, size, shortCode);

                var createdFile = ToUploadedFile(path, name, size, mimeType, shortCode, hash, deduplicated, now, now);
                await _notifications.NotifyFileCreated(bucketId, createdFile.ToBucketFile());
                return createdFile;
            }
        }
        catch
        {
            // Clean up temp file on failure
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
            throw;
        }
    }

    private static UploadedFile ToUploadedFile(string path, string name, long size, string mimeType,
        string? shortCode, string hash, bool deduplicated, DateTime createdAt, DateTime updatedAt) => new()
    {
        Path = path,
        Name = name,
        Size = size,
        MimeType = mimeType,
        ShortCode = shortCode,
        ShortUrl = shortCode != null ? $"/s/{shortCode}" : null,
        Sha256 = hash,
        Deduplicated = deduplicated,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };
}
