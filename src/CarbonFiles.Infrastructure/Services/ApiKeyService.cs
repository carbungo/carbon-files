using System.Security.Cryptography;
using System.Text;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly CarbonFilesDbContext _db;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(CarbonFilesDbContext db, ILogger<ApiKeyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiKeyResponse> CreateAsync(string name)
    {
        var (fullKey, prefix) = IdGenerator.GenerateApiKey();
        // fullKey = "cf4_{8hex}_{32hex}", prefix = "cf4_{8hex}"
        // Extract secret: everything after the prefix and the underscore separator
        var secret = fullKey[(prefix.Length + 1)..];
        var hashed = HashSecret(secret);

        var entity = new ApiKeyEntity
        {
            Prefix = prefix,
            HashedSecret = hashed,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created API key {Prefix} with name {Name}", prefix, name);

        return new ApiKeyResponse
        {
            Key = fullKey,
            Prefix = prefix,
            Name = name,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<PaginatedResponse<ApiKeyListItem>> ListAsync(PaginationParams pagination)
    {
        var total = await _db.ApiKeys.CountAsync();

        // Use raw SQL for the key query to avoid dynamic IQueryable chains,
        // and a separate query for bucket stats to avoid correlated subqueries.
        // Both patterns are rejected by the EF Core precompiler.

        // Sort column mapping (whitelist to prevent SQL injection)
        var sortColumn = pagination.Sort?.ToLowerInvariant() switch
        {
            "name" => "Name",
            "last_used_at" => "LastUsedAt",
            "created_at" => "CreatedAt",
            _ => "CreatedAt"
        };
        var isTotalSizeSort = pagination.Sort?.ToLowerInvariant() == "total_size";
        var sortDir = pagination.Order?.ToLowerInvariant() == "asc" ? "ASC" : "DESC";

        // Get bucket stats grouped by owner key prefix (avoids correlated subquery)
        var stats = await _db.Buckets
            .Where(b => b.OwnerKeyPrefix != null)
            .GroupBy(b => b.OwnerKeyPrefix!)
            .Select(g => new
            {
                Prefix = g.Key,
                BucketCount = g.Count(),
                FileCount = g.Sum(b => b.FileCount),
                TotalSize = g.Sum(b => b.TotalSize)
            })
            .ToListAsync();
        var statsLookup = stats.ToDictionary(s => s.Prefix);

        List<ApiKeyEntity> keys;
        if (isTotalSizeSort)
        {
            // For total_size sort, we need all keys and sort in memory
            // because the sort column comes from the stats join
            keys = await _db.ApiKeys.ToListAsync();
            keys = (sortDir == "ASC"
                ? keys.OrderBy(k => statsLookup.TryGetValue(k.Prefix, out var s) ? s.TotalSize : 0)
                : keys.OrderByDescending(k => statsLookup.TryGetValue(k.Prefix, out var s) ? s.TotalSize : 0))
                .Skip(pagination.Offset)
                .Take(pagination.Limit)
                .ToList();
        }
        else
        {
            var sql = $"SELECT * FROM ApiKeys ORDER BY {sortColumn} {sortDir} LIMIT {{0}} OFFSET {{1}}";
            keys = await _db.ApiKeys.FromSqlRaw(sql, pagination.Limit, pagination.Offset).ToListAsync();
        }

        var items = keys.Select(k =>
        {
            statsLookup.TryGetValue(k.Prefix, out var s);
            return k.ToListItem(s?.BucketCount ?? 0, s?.FileCount ?? 0, s?.TotalSize ?? 0);
        }).ToList();

        return new PaginatedResponse<ApiKeyListItem>
        {
            Items = items,
            Total = total,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    public async Task<bool> DeleteAsync(string prefix)
    {
        var entity = await _db.ApiKeys.FindAsync(prefix);
        if (entity == null)
        {
            _logger.LogDebug("API key {Prefix} not found for deletion", prefix);
            return false;
        }

        _db.ApiKeys.Remove(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted API key {Prefix}", prefix);

        return true;
    }

    public async Task<ApiKeyUsageResponse?> GetUsageAsync(string prefix)
    {
        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null)
        {
            _logger.LogDebug("API key {Prefix} not found for usage query", prefix);
            return null;
        }

        var buckets = await _db.Buckets
            .Where(b => b.OwnerKeyPrefix == prefix)
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

        var totalFiles = buckets.Sum(b => b.FileCount);
        var totalSize = buckets.Sum(b => b.TotalSize);
        var totalDownloads = await _db.Buckets
            .Where(b => b.OwnerKeyPrefix == prefix)
            .SumAsync(b => b.DownloadCount);

        return new ApiKeyUsageResponse
        {
            Prefix = entity.Prefix,
            Name = entity.Name,
            CreatedAt = entity.CreatedAt,
            LastUsedAt = entity.LastUsedAt,
            BucketCount = buckets.Count,
            FileCount = totalFiles,
            TotalSize = totalSize,
            TotalDownloads = totalDownloads,
            Buckets = buckets
        };
    }

    public async Task<(string Name, string Prefix)?> ValidateKeyAsync(string fullKey)
    {
        // Key format: cf4_{8hex}_{32hex}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4")
        {
            _logger.LogDebug("API key validation failed: invalid format");
            return null;
        }

        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null)
        {
            _logger.LogDebug("API key validation failed: prefix {Prefix} not found", prefix);
            return null;
        }

        var hashed = HashSecret(secret);
        if (hashed != entity.HashedSecret)
        {
            _logger.LogWarning("API key validation failed: invalid secret for prefix {Prefix}", prefix);
            return null;
        }

        return (entity.Name, entity.Prefix);
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(bytes);
    }
}
