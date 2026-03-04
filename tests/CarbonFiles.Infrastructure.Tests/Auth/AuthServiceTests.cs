using System.Security.Cryptography;
using System.Text;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Auth;
using CarbonFiles.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Auth;

public class AuthServiceTests : IDisposable
{
    private const string AdminKey = "test-admin-key";
    private const string JwtSecret = "test-jwt-secret";

    private readonly SqliteConnection _db;
    private readonly JwtHelper _jwt;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseInitializer.Initialize(_db);

        var options = Options.Create(new CarbonFilesOptions
        {
            AdminKey = AdminKey,
            JwtSecret = JwtSecret
        });

        _jwt = new JwtHelper(JwtSecret);
        var cache = new MemoryCache(new MemoryCacheOptions());

        _sut = new AuthService(_db, options, _jwt, cache, NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    [Fact]
    public async Task ResolveAsync_AdminKey_ReturnsAdminContext()
    {
        var result = await _sut.ResolveAsync(AdminKey);

        result.IsAdmin.Should().BeTrue();
        result.Role.Should().Be(AuthRole.Admin);
    }

    [Fact]
    public async Task ResolveAsync_ValidApiKey_ReturnsOwnerContext()
    {
        // Seed an API key
        var secret = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var prefix = "cf4_abcd1234";
        var hashedSecret = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        await Db.ExecuteAsync(_db,
            "INSERT INTO ApiKeys (Prefix, HashedSecret, Name, CreatedAt) VALUES (@Prefix, @HashedSecret, @Name, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Prefix", prefix);
                p.AddWithValue("@HashedSecret", hashedSecret);
                p.AddWithValue("@Name", "test-owner");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var fullKey = $"cf4_abcd1234_{secret}";
        var result = await _sut.ResolveAsync(fullKey);

        result.IsOwner.Should().BeTrue();
        result.OwnerName.Should().Be("test-owner");
        result.KeyPrefix.Should().Be(prefix);
    }

    [Fact]
    public async Task ResolveAsync_InvalidApiKey_ReturnsPublicContext()
    {
        var result = await _sut.ResolveAsync("cf4_badprefix_badsecretbadsecretbadsecretbadsecre");

        result.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_ValidJwt_ReturnsAdminContext()
    {
        var (token, _) = _jwt.CreateDashboardToken(DateTime.UtcNow.AddHours(1));

        var result = await _sut.ResolveAsync(token);

        result.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_NoToken_ReturnsPublicContext()
    {
        var result = await _sut.ResolveAsync(null);

        result.IsPublic.Should().BeTrue();
    }
}
