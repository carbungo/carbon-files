using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class ShortUrlServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ShortUrlService _sut;

    public ShortUrlServiceTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseInitializer.Initialize(_db);

        _sut = new ShortUrlService(_db, new NullCacheService(), NullLogger<ShortUrlService>.Instance);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Generates6CharCodeAndStoresIt()
    {
        await SeedBucketAsync("bucket0001");

        var code = await _sut.CreateAsync("bucket0001", "test.txt");

        code.Should().HaveLength(6);

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code",
            p => p.AddWithValue("@code", code),
            ShortUrlEntity.Read);
        entity.Should().NotBeNull();
        entity!.BucketId.Should().Be("bucket0001");
        entity.FilePath.Should().Be("test.txt");
        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── ResolveAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ReturnsCorrectUrl()
    {
        await SeedBucketAsync("bucket0002");
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "abc123");
                p.AddWithValue("@BucketId", "bucket0002");
                p.AddWithValue("@FilePath", "hello.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var url = await _sut.ResolveAsync("abc123");

        url.Should().Be("/api/buckets/bucket0002/files/hello.txt/content");
    }

    [Fact]
    public async Task ResolveAsync_ExpiredBucket_ReturnsNull()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "expired001");
                p.AddWithValue("@Name", "expired");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10));
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1));
            });
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "exp123");
                p.AddWithValue("@BucketId", "expired001");
                p.AddWithValue("@FilePath", "file.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var url = await _sut.ResolveAsync("exp123");

        url.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NonexistentCode_ReturnsNull()
    {
        var url = await _sut.ResolveAsync("zzzzzz");

        url.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_BucketDeleted_ReturnsNull()
    {
        // Short URL exists but bucket has been removed
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "orphan1");
                p.AddWithValue("@BucketId", "deleted001");
                p.AddWithValue("@FilePath", "orphan.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var url = await _sut.ResolveAsync("orphan1");

        url.Should().BeNull();
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesShortUrl()
    {
        await SeedBucketAsync("bucket0003", "admin");
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "del123");
                p.AddWithValue("@BucketId", "bucket0003");
                p.AddWithValue("@FilePath", "file.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("del123", auth);

        result.Should().BeTrue();
        var remaining = await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM ShortUrls WHERE Code = 'del123'");
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ReturnsFalse()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "bucket0004");
                p.AddWithValue("@Name", "alice-bucket");
                p.AddWithValue("@Owner", "alice");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            });
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "own123");
                p.AddWithValue("@BucketId", "bucket0004");
                p.AddWithValue("@FilePath", "file.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.DeleteAsync("own123", auth);

        result.Should().BeFalse();
        // Short URL should still exist
        var remaining = await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM ShortUrls WHERE Code = 'own123'");
        remaining.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_OwnerCanDelete()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "bucket0005");
                p.AddWithValue("@Name", "alice-bucket");
                p.AddWithValue("@Owner", "alice");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            });
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "own456");
                p.AddWithValue("@BucketId", "bucket0005");
                p.AddWithValue("@FilePath", "file.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.DeleteAsync("own456", auth);

        result.Should().BeTrue();
        var remaining = await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM ShortUrls WHERE Code = 'own456'");
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentCode_ReturnsFalse()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("nocode", auth);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_BucketDeleted_ReturnsFalse()
    {
        // Short URL exists but bucket is gone
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p =>
            {
                p.AddWithValue("@Code", "orphn2");
                p.AddWithValue("@BucketId", "deleted002");
                p.AddWithValue("@FilePath", "orphan.txt");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
            });

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("orphn2", auth);

        result.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedBucketAsync(string bucketId, string owner = "admin")
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", bucketId);
                p.AddWithValue("@Name", $"bucket-{bucketId}");
                p.AddWithValue("@Owner", owner);
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            });
    }
}
