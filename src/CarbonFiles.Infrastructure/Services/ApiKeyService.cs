using System.Data;
using System.Security.Cryptography;
using System.Text;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly IDbConnection _db;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(IDbConnection db, ILogger<ApiKeyService> logger)
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

        var now = DateTime.UtcNow;
        await Db.ExecuteAsync(_db,
            "INSERT INTO ApiKeys (Prefix, HashedSecret, Name, CreatedAt) VALUES (@Prefix, @HashedSecret, @Name, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Prefix", prefix);
                p.AddWithValue("@HashedSecret", hashed);
                p.AddWithValue("@Name", name);
                p.AddWithValue("@CreatedAt", now);
            });

        _logger.LogInformation("Created API key {Prefix} with name {Name}", prefix, name);

        return new ApiKeyResponse
        {
            Key = fullKey,
            Prefix = prefix,
            Name = name,
            CreatedAt = now
        };
    }

    public async Task<PaginatedResponse<ApiKeyListItem>> ListAsync(PaginationParams pagination)
    {
        var total = await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM ApiKeys");

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

        // Get bucket stats grouped by owner key prefix
        var stats = await Db.QueryAsync(_db,
            "SELECT OwnerKeyPrefix AS Prefix, COUNT(*) AS BucketCount, SUM(FileCount) AS FileCount, SUM(TotalSize) AS TotalSize FROM Buckets WHERE OwnerKeyPrefix IS NOT NULL GROUP BY OwnerKeyPrefix",
            null,
            r => new BucketStats
            {
                Prefix = r.GetString(r.GetOrdinal("Prefix")),
                BucketCount = r.GetInt32(r.GetOrdinal("BucketCount")),
                FileCount = r.GetInt32(r.GetOrdinal("FileCount")),
                TotalSize = r.GetInt64(r.GetOrdinal("TotalSize")),
            });
        var statsLookup = stats.ToDictionary(s => s.Prefix);

        List<ApiKeyEntity> keys;
        if (isTotalSizeSort)
        {
            // For total_size sort, we need all keys and sort in memory
            keys = await Db.QueryAsync(_db, "SELECT * FROM ApiKeys", null, ApiKeyEntity.Read);
            keys = (sortDir == "ASC"
                ? keys.OrderBy(k => statsLookup.TryGetValue(k.Prefix, out var s) ? s.TotalSize : 0)
                : keys.OrderByDescending(k => statsLookup.TryGetValue(k.Prefix, out var s) ? s.TotalSize : 0))
                .Skip(pagination.Offset)
                .Take(pagination.Limit)
                .ToList();
        }
        else
        {
            var sql = $"SELECT * FROM ApiKeys ORDER BY {sortColumn} {sortDir} LIMIT @Limit OFFSET @Offset";
            keys = await Db.QueryAsync(_db, sql,
                p =>
                {
                    p.AddWithValue("@Limit", pagination.Limit);
                    p.AddWithValue("@Offset", pagination.Offset);
                },
                ApiKeyEntity.Read);
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
        var rows = await Db.ExecuteAsync(_db, "DELETE FROM ApiKeys WHERE Prefix = @prefix",
            p => p.AddWithValue("@prefix", prefix));
        if (rows == 0)
        {
            _logger.LogDebug("API key {Prefix} not found for deletion", prefix);
            return false;
        }

        _logger.LogInformation("Deleted API key {Prefix}", prefix);
        return true;
    }

    public async Task<ApiKeyUsageResponse?> GetUsageAsync(string prefix)
    {
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @prefix",
            p => p.AddWithValue("@prefix", prefix),
            ApiKeyEntity.Read);
        if (entity == null)
        {
            _logger.LogDebug("API key {Prefix} not found for usage query", prefix);
            return null;
        }

        var buckets = await Db.QueryAsync(_db,
            """
            SELECT Id, Name, Owner, Description, CreatedAt, ExpiresAt, LastUsedAt, FileCount, TotalSize
            FROM Buckets WHERE OwnerKeyPrefix = @prefix
            """,
            p => p.AddWithValue("@prefix", prefix),
            r => new Bucket
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                Name = r.GetString(r.GetOrdinal("Name")),
                Owner = r.GetString(r.GetOrdinal("Owner")),
                Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                ExpiresAt = r.IsDBNull(r.GetOrdinal("ExpiresAt")) ? null : r.GetDateTime(r.GetOrdinal("ExpiresAt")),
                LastUsedAt = r.IsDBNull(r.GetOrdinal("LastUsedAt")) ? null : r.GetDateTime(r.GetOrdinal("LastUsedAt")),
                FileCount = r.GetInt32(r.GetOrdinal("FileCount")),
                TotalSize = r.GetInt64(r.GetOrdinal("TotalSize")),
            });

        var totalFiles = buckets.Sum(b => b.FileCount);
        var totalSize = buckets.Sum(b => b.TotalSize);
        var totalDownloads = await Db.ExecuteScalarAsync<long>(_db,
            "SELECT COALESCE(SUM(DownloadCount), 0) FROM Buckets WHERE OwnerKeyPrefix = @prefix",
            p => p.AddWithValue("@prefix", prefix));

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

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @prefix",
            p => p.AddWithValue("@prefix", prefix),
            ApiKeyEntity.Read);
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

    private sealed class BucketStats
    {
        public required string Prefix { get; set; }
        public int BucketCount { get; set; }
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
    }
}
