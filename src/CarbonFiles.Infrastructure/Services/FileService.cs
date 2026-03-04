using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class FileService : IFileService
{
    private readonly IDbConnection _db;
    private readonly FileStorageService _storage;
    private readonly ContentStorageService _contentStorage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<FileService> _logger;

    public FileService(IDbConnection db, FileStorageService storage, ContentStorageService contentStorage,
        INotificationService notifications, ICacheService cache, ILogger<FileService> logger)
    {
        _db = db;
        _storage = storage;
        _contentStorage = contentStorage;
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

    public async Task<DirectoryListingResponse> ListDirectoryAsync(string bucketId, string path, PaginationParams pagination)
    {
        // Normalize path: trim slashes, append '/' for non-root
        var trimmed = path.Trim('/');
        var prefix = trimmed.Length > 0 ? trimmed + "/" : "";
        var prefixLen = prefix.Length;

        _logger.LogDebug("Listing directory in bucket {BucketId} path={Path} prefix={Prefix}", bucketId, path, prefix);

        // Sort column mapping (whitelist to prevent SQL injection)
        var sortColumn = pagination.Sort?.ToLowerInvariant() switch
        {
            "name" => "Name",
            "path" => "Path",
            "size" => "Size",
            "mime_type" => "MimeType",
            "updated_at" => "UpdatedAt",
            "created_at" => "CreatedAt",
            _ => "Name"
        };
        var sortDir = pagination.Order?.ToLowerInvariant() == "desc" ? "DESC" : "ASC";

        // Direct files at this level (paginated)
        var filesSql = prefix.Length > 0
            ? $"SELECT * FROM Files WHERE BucketId = @bucketId AND Path LIKE @prefix || '%' AND INSTR(SUBSTR(Path, @prefixLen + 1), '/') = 0 ORDER BY {sortColumn} {sortDir} LIMIT @Limit OFFSET @Offset"
            : $"SELECT * FROM Files WHERE BucketId = @bucketId AND INSTR(Path, '/') = 0 ORDER BY {sortColumn} {sortDir} LIMIT @Limit OFFSET @Offset";

        var entities = await Db.QueryAsync(_db, filesSql,
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                if (prefix.Length > 0)
                {
                    p.AddWithValue("@prefix", prefix);
                    p.AddWithValue("@prefixLen", prefixLen);
                }
                p.AddWithValue("@Limit", pagination.Limit);
                p.AddWithValue("@Offset", pagination.Offset);
            },
            FileEntity.Read);
        var files = entities.Select(f => f.ToBucketFile()).ToList();

        // Count of direct files at this level
        var countSql = prefix.Length > 0
            ? "SELECT COUNT(*) FROM Files WHERE BucketId = @bucketId AND Path LIKE @prefix || '%' AND INSTR(SUBSTR(Path, @prefixLen + 1), '/') = 0"
            : "SELECT COUNT(*) FROM Files WHERE BucketId = @bucketId AND INSTR(Path, '/') = 0";

        var totalFiles = await Db.ExecuteScalarAsync<int>(_db, countSql,
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                if (prefix.Length > 0)
                {
                    p.AddWithValue("@prefix", prefix);
                    p.AddWithValue("@prefixLen", prefixLen);
                }
            });

        // Distinct folder names at this level (not paginated)
        var foldersSql = prefix.Length > 0
            ? "SELECT DISTINCT SUBSTR(SUBSTR(Path, @prefixLen + 1), 1, INSTR(SUBSTR(Path, @prefixLen + 1), '/') - 1) AS FolderName FROM Files WHERE BucketId = @bucketId AND Path LIKE @prefix || '%' AND INSTR(SUBSTR(Path, @prefixLen + 1), '/') > 0 ORDER BY FolderName ASC"
            : "SELECT DISTINCT SUBSTR(Path, 1, INSTR(Path, '/') - 1) AS FolderName FROM Files WHERE BucketId = @bucketId AND INSTR(Path, '/') > 0 ORDER BY FolderName ASC";

        var folders = await Db.QueryAsync(_db, foldersSql,
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                if (prefix.Length > 0)
                {
                    p.AddWithValue("@prefix", prefix);
                    p.AddWithValue("@prefixLen", prefixLen);
                }
            },
            r => r.GetString(0));

        return new DirectoryListingResponse
        {
            Files = files,
            Folders = folders,
            TotalFiles = totalFiles,
            TotalFolders = folders.Count,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    public async Task<FileTreeResponse> ListTreeAsync(string bucketId, string? prefix, string delimiter, int limit, string? cursor)
    {
        prefix ??= "";

        var sql = cursor != null
            ? "SELECT Path, Size FROM Files WHERE BucketId = @bucketId AND Path > @cursor AND Path LIKE @likePrefix ORDER BY Path LIMIT @fetchLimit"
            : "SELECT Path, Size FROM Files WHERE BucketId = @bucketId AND Path LIKE @likePrefix ORDER BY Path LIMIT @fetchLimit";

        var likePrefix = prefix.Replace("%", "[%]").Replace("_", "[_]") + "%";
        var fetchLimit = limit * 10 + 100;

        var allEntries = await Db.QueryAsync(_db, sql,
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@likePrefix", likePrefix);
                p.AddWithValue("@fetchLimit", fetchLimit);
                if (cursor != null)
                    p.AddWithValue("@cursor", cursor);
            },
            r => (Path: r.GetString(0), Size: r.GetInt64(1)));

        var files = new List<string>();
        var dirStats = new Dictionary<string, (int Count, long Size)>();

        foreach (var (path, size) in allEntries)
        {
            var remainder = path[prefix.Length..];
            var delimIndex = remainder.IndexOf(delimiter, StringComparison.Ordinal);

            if (delimIndex < 0)
            {
                files.Add(path);
            }
            else
            {
                var dirName = prefix + remainder[..(delimIndex + delimiter.Length)];
                if (dirStats.TryGetValue(dirName, out var stats))
                    dirStats[dirName] = (stats.Count + 1, stats.Size + size);
                else
                    dirStats[dirName] = (1, size);
            }
        }

        var filePaths = files.Take(limit).ToList();
        var fileEntities = new List<BucketFile>();
        if (filePaths.Count > 0)
        {
            foreach (var fp in filePaths)
            {
                var entity = await Db.QueryFirstOrDefaultAsync(_db,
                    "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
                    p =>
                    {
                        p.AddWithValue("@bucketId", bucketId);
                        p.AddWithValue("@path", fp);
                    },
                    FileEntity.Read);
                if (entity != null)
                    fileEntities.Add(entity.ToBucketFile());
            }
        }

        var directories = dirStats
            .OrderBy(d => d.Key)
            .Select(d => new DirectoryEntry { Path = d.Key, FileCount = d.Value.Count, TotalSize = d.Value.Size })
            .ToList();

        string? nextCursor = null;
        if (files.Count > limit || allEntries.Count >= fetchLimit)
            nextCursor = filePaths.LastOrDefault() ?? allEntries.LastOrDefault().Path;

        return new FileTreeResponse
        {
            Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
            Delimiter = delimiter,
            Directories = directories,
            Files = fileEntities,
            TotalFiles = files.Count,
            TotalDirectories = directories.Count,
            Cursor = nextCursor,
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

    public async Task<string?> GetContentDiskPathAsync(string bucketId, string path)
    {
        var hash = await Db.ExecuteScalarAsync<string?>(_db,
            "SELECT ContentHash FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            });
        if (hash == null) return null;

        var diskPath = await Db.ExecuteScalarAsync<string?>(_db,
            "SELECT DiskPath FROM ContentObjects WHERE Hash = @hash",
            p => p.AddWithValue("@hash", hash));

        return diskPath;
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

        // Decrement content ref count for CAS files
        if (entity.ContentHash != null)
        {
            await Db.ExecuteAsync(_db,
                "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @hash",
                p => p.AddWithValue("@hash", entity.ContentHash), tx);
        }

        tx.Commit();

        // Only delete old per-bucket disk file for pre-migration files
        if (entity.ContentHash == null)
        {
            _storage.DeleteFile(bucketId, path);
        }

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

    public async Task<bool> PatchFileAsync(string bucketId, string path, Stream patchContent, long offset, bool append)
    {
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            },
            FileEntity.Read);
        if (entity?.ContentHash == null) return false;

        var contentObj = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ContentObjects WHERE Hash = @hash",
            p => p.AddWithValue("@hash", entity.ContentHash),
            ContentObjectEntity.Read);
        if (contentObj == null) return false;

        // Read original content, apply patch, write to new temp, hash result
        var originalPath = _contentStorage.GetFullPath(contentObj.DiskPath);
        var (tempPath, newSize, newHash) = await ApplyPatchToTempAsync(originalPath, patchContent, offset, append);

        try
        {
            var now = DateTime.UtcNow;
            var oldHash = entity.ContentHash;

            using var tx = _db.BeginTransaction();

            // Check if new content already exists
            var existingNew = await Db.QueryFirstOrDefaultAsync(_db,
                "SELECT * FROM ContentObjects WHERE Hash = @hash",
                p => p.AddWithValue("@hash", newHash),
                ContentObjectEntity.Read);

            if (existingNew != null)
            {
                File.Delete(tempPath);
                await Db.ExecuteAsync(_db,
                    "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                    p => p.AddWithValue("@hash", newHash), tx);
            }
            else
            {
                var diskPath = ContentStorageService.ComputeDiskPath(newHash);
                _contentStorage.MoveToContentStore(tempPath, diskPath);
                await Db.ExecuteAsync(_db,
                    "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                    p =>
                    {
                        p.AddWithValue("@hash", newHash);
                        p.AddWithValue("@size", newSize);
                        p.AddWithValue("@diskPath", diskPath);
                        p.AddWithValue("@now", now);
                    }, tx);
            }

            // Decrement old content ref
            if (oldHash != newHash)
            {
                await Db.ExecuteAsync(_db,
                    "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @oldHash",
                    p => p.AddWithValue("@oldHash", oldHash), tx);
            }

            // Update file record
            await Db.ExecuteAsync(_db,
                "UPDATE Files SET Size = @size, ContentHash = @hash, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @path",
                p =>
                {
                    p.AddWithValue("@size", newSize);
                    p.AddWithValue("@hash", newHash);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                    p.AddWithValue("@path", path);
                }, tx);

            // Update bucket size
            await Db.ExecuteAsync(_db,
                "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize + @newSize) WHERE Id = @bucketId",
                p =>
                {
                    p.AddWithValue("@oldSize", entity.Size);
                    p.AddWithValue("@newSize", newSize);
                    p.AddWithValue("@bucketId", bucketId);
                }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, path);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            return true;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static async Task<(string TempPath, long Size, string Hash)> ApplyPatchToTempAsync(
        string originalPath, Stream patchContent, long offset, bool append)
    {
        var tempDir = Path.GetTempPath();
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tmp");

        using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        await using var outFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        await using var inFile = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);

        var buffer = new byte[81920];
        long totalBytes = 0;

        if (append)
        {
            int read;
            while ((read = await inFile.ReadAsync(buffer)) > 0)
            {
                sha256.AppendData(buffer.AsSpan(0, read));
                await outFile.WriteAsync(buffer.AsMemory(0, read));
                totalBytes += read;
            }
            while ((read = await patchContent.ReadAsync(buffer)) > 0)
            {
                sha256.AppendData(buffer.AsSpan(0, read));
                await outFile.WriteAsync(buffer.AsMemory(0, read));
                totalBytes += read;
            }
        }
        else
        {
            // Copy up to offset
            long copied = 0;
            while (copied < offset)
            {
                var toRead = (int)Math.Min(buffer.Length, offset - copied);
                var read = await inFile.ReadAsync(buffer.AsMemory(0, toRead));
                if (read == 0) break;
                sha256.AppendData(buffer.AsSpan(0, read));
                await outFile.WriteAsync(buffer.AsMemory(0, read));
                copied += read;
                totalBytes += read;
            }
            // Skip original content at the patch region, write patch content
            long patchBytes = 0;
            int patchRead;
            while ((patchRead = await patchContent.ReadAsync(buffer)) > 0)
            {
                sha256.AppendData(buffer.AsSpan(0, patchRead));
                await outFile.WriteAsync(buffer.AsMemory(0, patchRead));
                patchBytes += patchRead;
                totalBytes += patchRead;
            }
            // Skip corresponding bytes in original, copy remainder
            inFile.Seek(offset + patchBytes, SeekOrigin.Begin);
            int tailRead;
            while ((tailRead = await inFile.ReadAsync(buffer)) > 0)
            {
                sha256.AppendData(buffer.AsSpan(0, tailRead));
                await outFile.WriteAsync(buffer.AsMemory(0, tailRead));
                totalBytes += tailRead;
            }
        }

        var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());
        return (tempPath, totalBytes, hashHex);
    }
}
