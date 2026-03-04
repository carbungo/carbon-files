using System.Text;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class BucketService : IBucketService
{
    private readonly CarbonFilesDbContext _db;
    private readonly string _dataDir;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<BucketService> _logger;

    public BucketService(CarbonFilesDbContext db, IOptions<CarbonFilesOptions> options, INotificationService notifications, ICacheService cache, ILogger<BucketService> logger)
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

        var entity = new BucketEntity
        {
            Id = bucketId,
            Name = request.Name,
            Owner = owner,
            OwnerKeyPrefix = keyPrefix,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };

        _db.Buckets.Add(entity);
        await _db.SaveChangesAsync();

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

        // Use raw SQL to avoid dynamic IQueryable chains that the EF Core precompiler rejects
        var whereClauses = new List<string>();
        var parameters = new List<object>();
        var paramIndex = 0;

        if (auth.IsOwner)
        {
            whereClauses.Add($"Owner = {{{paramIndex}}}");
            parameters.Add(auth.OwnerName!);
            paramIndex++;
        }

        if (!includeExpired)
        {
            whereClauses.Add($"(ExpiresAt IS NULL OR ExpiresAt > {{{paramIndex}}})");
            parameters.Add(DateTime.UtcNow);
            paramIndex++;
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // Count query
        var countSql = $"SELECT COUNT(*) AS \"Value\" FROM Buckets {whereClause}";
        var total = await _db.Database.SqlQueryRaw<int>(countSql, parameters.ToArray()).FirstAsync();

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

        var dataSql = $"SELECT * FROM Buckets {whereClause} ORDER BY {sortColumn} {sortDir} LIMIT {{{paramIndex}}} OFFSET {{{paramIndex + 1}}}";
        parameters.Add(pagination.Limit);
        parameters.Add(pagination.Offset);

        var entities = await _db.Buckets.FromSqlRaw(dataSql, parameters.ToArray()).ToListAsync();
        var items = entities.Select(b => b.ToBucket()).ToList();

        return new PaginatedResponse<Bucket>
        {
            Items = items,
            Total = total,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    public async Task<BucketDetailResponse?> GetByIdAsync(string id)
    {
        var cached = _cache.GetBucket(id);
        if (cached != null)
            return cached;

        var entity = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == id);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found", id);
            return null;
        }

        // Expired buckets are not accessible
        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
        {
            _logger.LogDebug("Bucket {BucketId} is expired", id);
            return null;
        }

        var files = await _db.Files
            .Where(f => f.BucketId == id)
            .OrderBy(f => f.Path)
            .Take(101) // Take 101 to detect has_more_files
            .ToListAsync();

        var hasMore = files.Count > 100;
        var fileList = files.Take(100).Select(f => f.ToBucketFile()).ToList();

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
            Files = fileList,
            HasMoreFiles = hasMore
        };
        _cache.SetBucket(id, response);
        return response;
    }

    public async Task<Bucket?> GetBucketAsync(string id)
    {
        var entity = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == id);
        if (entity == null)
            return null;

        // Expired buckets are not accessible
        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
            return null;

        return entity.ToBucket();
    }

    public async Task<List<BucketFile>> GetAllFilesAsync(string id, CancellationToken ct = default)
    {
        var files = await _db.Files
            .Where(f => f.BucketId == id)
            .OrderBy(f => f.Path)
            .ToListAsync(ct);

        return files.Select(f => f.ToBucketFile()).ToList();
    }

    public async Task<Bucket?> UpdateAsync(string id, UpdateBucketRequest request, AuthContext auth)
    {
        var entity = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == id);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found for update", id);
            return null;
        }

        // Check ownership
        if (!auth.CanManage(entity.Owner))
        {
            _logger.LogWarning("Access denied: update bucket {BucketId} by {Owner}", id, auth.OwnerName ?? "unknown");
            return null;
        }

        if (request.Name != null)
            entity.Name = request.Name;

        if (request.Description != null)
            entity.Description = request.Description;

        if (request.ExpiresIn != null)
            entity.ExpiresAt = ExpiryParser.Parse(request.ExpiresIn);

        await _db.SaveChangesAsync();
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
        var entity = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == id);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found for delete", id);
            return false;
        }

        // Check ownership
        if (!auth.CanManage(entity.Owner))
        {
            _logger.LogWarning("Access denied: delete bucket {BucketId} by {Owner}", id, auth.OwnerName ?? "unknown");
            return false;
        }

        // Delete all related entities
        var files = await _db.Files.Where(f => f.BucketId == id).ToListAsync();
        _db.Files.RemoveRange(files);

        var shortUrls = await _db.ShortUrls.Where(s => s.BucketId == id).ToListAsync();
        _db.ShortUrls.RemoveRange(shortUrls);

        var uploadTokens = await _db.UploadTokens.Where(t => t.BucketId == id).ToListAsync();
        _db.UploadTokens.RemoveRange(uploadTokens);

        _db.Buckets.Remove(entity);
        await _db.SaveChangesAsync();

        // Delete the bucket directory from disk
        var bucketDir = Path.Combine(_dataDir, id);
        if (Directory.Exists(bucketDir))
            Directory.Delete(bucketDir, true);

        _logger.LogInformation("Deleted bucket {BucketId} with {FileCount} files, {ShortUrlCount} short URLs, {TokenCount} upload tokens",
            id, files.Count, shortUrls.Count, uploadTokens.Count);

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
        var entity = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == id);
        if (entity == null)
        {
            _logger.LogDebug("Bucket {BucketId} not found for summary", id);
            return null;
        }

        // Expired buckets are not accessible
        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
        {
            _logger.LogDebug("Bucket {BucketId} is expired (summary)", id);
            return null;
        }

        var files = await _db.Files
            .Where(f => f.BucketId == id)
            .OrderBy(f => f.Path)
            .ToListAsync();

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
