using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IDbConnection _db;
    private readonly CarbonFilesOptions _options;
    private readonly JwtHelper _jwt;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public AuthService(IDbConnection db, IOptions<CarbonFilesOptions> options, JwtHelper jwt, IMemoryCache cache, ILogger<AuthService> logger)
    {
        _db = db;
        _options = options.Value;
        _jwt = jwt;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AuthContext> ResolveAsync(string? bearerToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
        {
            _logger.LogDebug("No bearer token provided, resolving as public");
            return AuthContext.Public();
        }

        // 1. Check admin key
        if (bearerToken == _options.AdminKey)
        {
            _logger.LogInformation("Admin key authenticated");
            return AuthContext.Admin();
        }

        // 2. Check API key (cf4_ prefix)
        if (bearerToken.StartsWith("cf4_"))
        {
            var cacheKey = $"apikey:{bearerToken}";
            if (_cache.TryGetValue(cacheKey, out (string Name, string Prefix) cached))
            {
                _logger.LogDebug("API key {Prefix} resolved from cache", cached.Prefix);
                return AuthContext.Owner(cached.Name, cached.Prefix);
            }

            var result = await ValidateApiKeyAsync(bearerToken);
            if (result != null)
            {
                _cache.Set(cacheKey, result.Value, CacheDuration);
                _logger.LogInformation("API key {Prefix} authenticated for {Name}", result.Value.Prefix, result.Value.Name);
                return AuthContext.Owner(result.Value.Name, result.Value.Prefix);
            }
            _logger.LogWarning("Invalid API key attempted with prefix {Prefix}", bearerToken.Split('_', 3) is [_, var p, ..] ? $"cf4_{p}" : "unknown");
            return AuthContext.Public(); // Invalid API key
        }

        // 3. Check dashboard JWT
        var (isValid, _) = _jwt.ValidateToken(bearerToken);
        if (isValid)
        {
            _logger.LogInformation("Dashboard JWT authenticated");
            return AuthContext.Admin();
        }

        _logger.LogDebug("Bearer token did not match any auth method");
        return AuthContext.Public();
    }

    private async Task<(string Name, string Prefix)?> ValidateApiKeyAsync(string fullKey)
    {
        // cf4_{8hex}_{32hex}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4") return null;

        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @prefix",
            p => p.AddWithValue("@prefix", prefix),
            ApiKeyEntity.Read);
        if (entity == null) return null;

        var hashed = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret)));

        if (hashed != entity.HashedSecret) return null;

        // Update last_used_at
        await Db.ExecuteAsync(_db,
            "UPDATE ApiKeys SET LastUsedAt = @now WHERE Prefix = @prefix",
            p =>
            {
                p.AddWithValue("@now", DateTime.UtcNow);
                p.AddWithValue("@prefix", prefix);
            });

        return (entity.Name, entity.Prefix);
    }
}
