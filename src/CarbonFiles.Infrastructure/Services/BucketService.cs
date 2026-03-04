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

        IQueryable<BucketEntity> query = _db.Buckets;

        // Filter by ownership
        if (auth.IsOwner)
            query = query.Where(b => b.Owner == auth.OwnerName);
        // Admin sees all; Public should not reach here (endpoint guards)

        // Exclude expired unless requested (admin only)
        if (!includeExpired)
            query = query.Where(b => b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow);

        var total = await query.CountAsync();

        // Apply sorting
        query = (pagination.Sort?.ToLowerInvariant(), pagination.Order?.ToLowerInvariant()) switch
        {
            ("name", "asc") => query.OrderBy(b => b.Name),
            ("name", _) => query.OrderByDescending(b => b.Name),
            ("expires_at", "asc") => query.OrderBy(b => b.ExpiresAt),
            ("expires_at", _) => query.OrderByDescending(b => b.ExpiresAt),
            ("last_used_at", "asc") => query.OrderBy(b => b.LastUsedAt),
            ("last_used_at", _) => query.OrderByDescending(b => b.LastUsedAt),
            ("total_size", "asc") => query.OrderBy(b => b.TotalSize),
            ("total_size", _) => query.OrderByDescending(b => b.TotalSize),
            ("created_at", "asc") => query.OrderBy(b => b.CreatedAt),
            _ => query.OrderByDescending(b => b.CreatedAt), // default: created_at desc
        };

        var items = await query
            .Skip(pagination.Offset)
            .Take(pagination.Limit)
            .Select(b => new Bucket
            {
                Id = b.Id,
                Name = b.Name,
                Owner = b.Owner,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                ExpiresAt = b.ExpiresAt,
                LastUsedAt = b.LastUsedAt,
                FileCount = b.FileCount,
                TotalSize = b.TotalSize
            })
            .ToListAsync();

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
