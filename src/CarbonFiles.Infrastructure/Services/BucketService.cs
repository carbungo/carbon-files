using System.Data;
using System.Text;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class BucketService : IBucketService
{
    private readonly IDbConnection _db;
    private readonly string _dataDir;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<BucketService> _logger;

    public BucketService(IDbConnection db, IOptions<CarbonFilesOptions> options, INotificationService notifications, ICacheService cache, ILogger<BucketService> logger)
    {
        _db = db;
        _dataDir = options.Value.DataDir;
        _notifications = notifications;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Bucket> CreateAsync(CreateBucketRequest request, AuthContext auth)
    {
        var bucketId = IdGenerator.GenerateBucketId();
        var expiresAt = ExpiryParser.Parse(request.ExpiresIn);

        var owner = auth.IsOwner ? auth.OwnerName! : "admin";
        var keyPrefix = auth.IsOwner ? auth.KeyPrefix : null;

        var now = DateTime.UtcNow;
        var entity = new BucketEntity
        {
            Id = bucketId,
            Name = request.Name,
            Owner = owner,
            OwnerKeyPrefix = keyPrefix,
            Description = request.Description,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, Description, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @Description, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", entity.Id);
                p.AddWithValue("@Name", entity.Name);
                p.AddWithValue("@Owner", entity.Owner);
                p.AddWithValue("@OwnerKeyPrefix", (object?)entity.OwnerKeyPrefix ?? DBNull.Value);
                p.AddWithValue("@Description", (object?)entity.Description ?? DBNull.Value);
                p.AddWithValue("@CreatedAt", entity.CreatedAt);
                p.AddWithValue("@ExpiresAt", (object?)entity.ExpiresAt ?? DBNull.Value);
            });

        _logger.LogInformation("Created bucket {BucketId} with name {Name} for owner {Owner}, expires {ExpiresAt}",
            bucketId, request.Name, owner, expiresAt?.ToString("o") ?? "never");

        // Create the storage directory on disk
        var bucketDir = Path.Combine(_dataDir, bucketId);
        Directory.CreateDirectory(bucketDir);

        var bucket = entity.ToBucket();
        await _notifications.NotifyBucketCreated(bucket);
        _cache.InvalidateStats();
        return bucket;
    }

    public async Task<PaginatedResponse<Bucket>> ListAsync(PaginationParams pagination, AuthContext auth, bool includeExpired = false)
    {
        _logger.LogDebug("Listing buckets for {AuthType} (includeExpired={IncludeExpired}, limit={Limit}, offset={Offset})",
            auth.IsAdmin ? "admin" : auth.OwnerName ?? "public", includeExpired, pagination.Limit, pagination.Offset);

        var whereClauses = new List<string>();

        if (auth.IsOwner)
            whereClauses.Add("Owner = @Owner");

        if (!includeExpired)
            whereClauses.Add("(ExpiresAt IS NULL OR ExpiresAt > @Now)");

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // Sort column mapping (whitelist to prevent SQL injection)
        var sortColumn = pagination.Sort?.ToLowerInvariant() switch
        {
            "name" => "Name",
            "expires_at" => "ExpiresAt",
            "last_used_at" => "LastUsedAt",
            "total_size" => "TotalSize",
            "created_at" => "CreatedAt",
            _ => "CreatedAt"
        };
        var sortDir = pagination.Order?.ToLowerInvariant() == "asc" ? "ASC" : "DESC";

        void AddParams(SqliteParameterCollection p)
        {
            if (auth.IsOwner)
                p.AddWithValue("@Owner", auth.OwnerName!);
            if (!includeExpired)
                p.AddWithValue("@Now", DateTime.UtcNow);
        }

        // Count query
        var total = await Db.ExecuteScalarAsync<int>(_db,
            $"SELECT COUNT(*) FROM Buckets {whereClause}",
            AddParams);

        var dataSql = $"SELECT * FROM Buckets {whereClause} ORDER BY {sortColumn} {sortDir} LIMIT @Limit OFFSET @Offset";
        var entities = await Db.QueryAsync(_db, dataSql,
            p =>
            {
                AddParams(p);
                p.AddWithValue("@Limit", pagination.Limit);
                p.AddWithValue("@Offset", pagination.Offset);
            },
            BucketEntity.Read);
        var items = entities.Select(b => b.ToBucket()).ToList();

        return new PaginatedResponse<Bucket>
        {
            Items = items,
            Total = total,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    /// <summary>
    /// Fetches a bucket entity by ID, returning null if not found or expired.
    /// </summary>
    private async Task<BucketEntity?> FetchActiveBucketEntityAsync(string id)
    {
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @id",
            p => p.AddWithValue("@id", id),
            BucketEntity.Read);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found", id);
            return null;
        }
        if (entity.IsExpired)
        {
            _logger.LogDebug("Bucket {BucketId} is expired", id);
            return null;
        }
        return entity;
    }

    /// <summary>
    /// Fetches a bucket entity for mutation, returning null if not found or caller lacks permission.
    /// Does not check expiry (expired buckets can still be updated/deleted by their owner).
    /// </summary>
    private async Task<BucketEntity?> FetchBucketForMutationAsync(string id, AuthContext auth)
    {
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @id",
            p => p.AddWithValue("@id", id),
            BucketEntity.Read);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found", id);
            return null;
        }
        if (!auth.CanManage(entity.Owner))
        {
            _logger.LogWarning("Access denied: bucket {BucketId} by {Owner}", id, auth.OwnerName ?? "unknown");
            return null;
        }
        return entity;
    }

    public async Task<BucketDetailResponse?> GetByIdAsync(string id, bool includeFiles = false)
    {
        var cached = _cache.GetBucket(id);
        if (cached != null)
            return cached;

        var entity = await FetchActiveBucketEntityAsync(id);
        if (entity == null)
            return null;

        // Dedup stats
        var uniqueContentCount = await Db.ExecuteScalarAsync<int>(_db,
            "SELECT COUNT(DISTINCT ContentHash) FROM Files WHERE BucketId = @id AND ContentHash IS NOT NULL",
            p => p.AddWithValue("@id", id));
        var uniqueContentSize = await Db.ExecuteScalarAsync<long>(_db,
            "SELECT COALESCE(SUM(co.Size), 0) FROM ContentObjects co WHERE co.Hash IN (SELECT DISTINCT ContentHash FROM Files WHERE BucketId = @id AND ContentHash IS NOT NULL)",
            p => p.AddWithValue("@id", id));

        IReadOnlyList<BucketFile>? fileList = null;
        bool? hasMore = null;

        if (includeFiles)
        {
            var files = await Db.QueryAsync(_db,
                "SELECT * FROM Files WHERE BucketId = @id ORDER BY Path LIMIT 101",
                p => p.AddWithValue("@id", id),
                FileEntity.Read);
            hasMore = files.Count > 100;
            fileList = files.Take(100).Select(f => f.ToBucketFile()).ToList();
        }

        var response = new BucketDetailResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Owner = entity.Owner,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            LastUsedAt = entity.LastUsedAt,
            FileCount = entity.FileCount,
            TotalSize = entity.TotalSize,
            UniqueContentCount = uniqueContentCount,
            UniqueContentSize = uniqueContentSize,
            Files = fileList,
            HasMoreFiles = hasMore
        };
        _cache.SetBucket(id, response);
        return response;
    }

    public async Task<Bucket?> GetBucketAsync(string id)
    {
        var entity = await FetchActiveBucketEntityAsync(id);
        return entity?.ToBucket();
    }

    public async Task<List<BucketFile>> GetAllFilesAsync(string id, CancellationToken ct = default)
    {
        var files = await Db.QueryAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @id ORDER BY Path",
            p => p.AddWithValue("@id", id),
            FileEntity.Read,
            ct: ct);

        return files.Select(f => f.ToBucketFile()).ToList();
    }

    public async Task<Bucket?> UpdateAsync(string id, UpdateBucketRequest request, AuthContext auth)
    {
        var entity = await FetchBucketForMutationAsync(id, auth);
        if (entity == null)
            return null;

        if (request.Name != null)
            entity.Name = request.Name;

        if (request.Description != null)
            entity.Description = request.Description;

        if (request.ExpiresIn != null)
            entity.ExpiresAt = ExpiryParser.Parse(request.ExpiresIn);

        await Db.ExecuteAsync(_db,
            "UPDATE Buckets SET Name = @Name, Description = @Description, ExpiresAt = @ExpiresAt WHERE Id = @Id",
            p =>
            {
                p.AddWithValue("@Name", entity.Name);
                p.AddWithValue("@Description", (object?)entity.Description ?? DBNull.Value);
                p.AddWithValue("@ExpiresAt", (object?)entity.ExpiresAt ?? DBNull.Value);
                p.AddWithValue("@Id", entity.Id);
            });

        _cache.InvalidateBucket(id);
        _cache.InvalidateStats();

        _logger.LogInformation("Updated bucket {BucketId}", id);

        var changes = new BucketChanges
        {
            Name = request.Name,
            Description = request.Description,
            ExpiresAt = request.ExpiresIn != null ? entity.ExpiresAt : null
        };
        await _notifications.NotifyBucketUpdated(id, changes);

        return entity.ToBucket();
    }

    public async Task<bool> DeleteAsync(string id, AuthContext auth)
    {
        var entity = await FetchBucketForMutationAsync(id, auth);
        if (entity == null)
            return false;

        // Delete all related entities in a transaction
        using var tx = _db.BeginTransaction();

        var fileCount = await Db.ExecuteAsync(_db, "DELETE FROM Files WHERE BucketId = @id",
            p => p.AddWithValue("@id", id), tx);
        var shortUrlCount = await Db.ExecuteAsync(_db, "DELETE FROM ShortUrls WHERE BucketId = @id",
            p => p.AddWithValue("@id", id), tx);
        var tokenCount = await Db.ExecuteAsync(_db, "DELETE FROM UploadTokens WHERE BucketId = @id",
            p => p.AddWithValue("@id", id), tx);
        await Db.ExecuteAsync(_db, "DELETE FROM Buckets WHERE Id = @id",
            p => p.AddWithValue("@id", id), tx);

        tx.Commit();

        // Delete the bucket directory from disk (best-effort after DB commit)
        var bucketDir = Path.Combine(_dataDir, id);
        try
        {
            if (Directory.Exists(bucketDir))
                Directory.Delete(bucketDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bucket {BucketId} deleted from DB but disk cleanup failed for {Dir}", id, bucketDir);
        }

        _logger.LogInformation("Deleted bucket {BucketId} with {FileCount} files, {ShortUrlCount} short URLs, {TokenCount} upload tokens",
            id, fileCount, shortUrlCount, tokenCount);

        await _notifications.NotifyBucketDeleted(id);
        _cache.InvalidateBucket(id);
        _cache.InvalidateFilesForBucket(id);
        _cache.InvalidateShortUrlsForBucket(id);
        _cache.InvalidateUploadTokensForBucket(id);
        _cache.InvalidateStats();
        return true;
    }

    public async Task<string?> GetSummaryAsync(string id)
    {
        var entity = await FetchActiveBucketEntityAsync(id);
        if (entity == null)
            return null;

        var files = await Db.QueryAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @id ORDER BY Path",
            p => p.AddWithValue("@id", id),
            FileEntity.Read);

        var sb = new StringBuilder();
        sb.AppendLine($"Bucket: {entity.Name}");
        sb.AppendLine($"Owner: {entity.Owner}");
        sb.AppendLine($"Files: {entity.FileCount} ({FormatSize(entity.TotalSize)})");
        sb.AppendLine($"Created: {entity.CreatedAt:yyyy-MM-dd}");
        sb.AppendLine($"Expires: {(entity.ExpiresAt.HasValue ? entity.ExpiresAt.Value.ToString("yyyy-MM-dd") : "never")}");
        sb.AppendLine();
        sb.AppendLine("Files:");

        foreach (var file in files)
            sb.AppendLine($"  {file.Path} ({FormatSize(file.Size)})");

        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}
