using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

internal sealed class NullNotificationService : INotificationService
{
    public Task NotifyFileCreated(string bucketId, BucketFile file) => Task.CompletedTask;
    public Task NotifyFileUpdated(string bucketId, BucketFile file) => Task.CompletedTask;
    public Task NotifyFileDeleted(string bucketId, string path) => Task.CompletedTask;
    public Task NotifyBucketCreated(Bucket bucket) => Task.CompletedTask;
    public Task NotifyBucketUpdated(string bucketId, BucketChanges changes) => Task.CompletedTask;
    public Task NotifyBucketDeleted(string bucketId) => Task.CompletedTask;
}

internal sealed class NullCacheService : ICacheService
{
    public BucketDetailResponse? GetBucket(string id) => null;
    public void SetBucket(string id, BucketDetailResponse bucket) { }
    public void InvalidateBucket(string id) { }
    public BucketFile? GetFileMetadata(string bucketId, string path) => null;
    public void SetFileMetadata(string bucketId, string path, BucketFile file) { }
    public void InvalidateFile(string bucketId, string path) { }
    public void InvalidateFilesForBucket(string bucketId) { }
    public (string BucketId, string FilePath)? GetShortUrl(string code) => null;
    public void SetShortUrl(string code, string bucketId, string filePath) { }
    public void InvalidateShortUrl(string code) { }
    public void InvalidateShortUrlsForBucket(string bucketId) { }
    public (string BucketId, bool IsValid)? GetUploadToken(string token) => null;
    public void SetUploadToken(string token, string bucketId, bool isValid) { }
    public void InvalidateUploadToken(string token) { }
    public void InvalidateUploadTokensForBucket(string bucketId) { }
    public StatsResponse? GetStats() => null;
    public void SetStats(StatsResponse stats) { }
    public void InvalidateStats() { }
}

public class BucketServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly BucketService _sut;
    private readonly string _tempDir;

    public BucketServiceTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseInitializer.Initialize(_db);

        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new CarbonFilesOptions { DataDir = _tempDir });
        _sut = new BucketService(_db, options, new NullNotificationService(), new NullCacheService(), NullLogger<BucketService>.Instance);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AdminAuth_SetsOwnerToAdmin()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "admin-bucket" };

        var result = await _sut.CreateAsync(request, auth);

        result.Owner.Should().Be("admin");
        result.Name.Should().Be("admin-bucket");
        result.Id.Should().HaveLength(10);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_OwnerAuth_SetsOwnerName()
    {
        var auth = AuthContext.Owner("my-agent", "cf4_12345678");
        var request = new CreateBucketRequest { Name = "agent-bucket" };

        var result = await _sut.CreateAsync(request, auth);

        result.Owner.Should().Be("my-agent");
    }

    [Fact]
    public async Task CreateAsync_CreatesDirectoryOnDisk()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "dir-test" };

        var result = await _sut.CreateAsync(request, auth);

        var bucketDir = Path.Combine(_tempDir, result.Id);
        Directory.Exists(bucketDir).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_ParsesExpiresIn()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "expiry-test", ExpiresIn = "1d" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_NeverExpiry_SetsNull()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "never-expire", ExpiresIn = "never" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_DefaultExpiry_Is1Week()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "default-expire" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_InvalidExpiry_ThrowsArgumentException()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "bad-expiry", ExpiresIn = "xyz" };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request, auth));
    }

    [Fact]
    public async Task CreateAsync_StoresEntityInDatabase()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "stored", Description = "desc" };

        var result = await _sut.CreateAsync(request, auth);

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @Id",
            p => p.AddWithValue("@Id", result.Id),
            BucketEntity.Read);
        entity.Should().NotBeNull();
        entity!.Name.Should().Be("stored");
        entity.Description.Should().Be("desc");
        entity.Owner.Should().Be("admin");
    }

    [Fact]
    public async Task CreateAsync_OwnerAuth_SetsKeyPrefix()
    {
        var auth = AuthContext.Owner("agent", "cf4_aabbccdd");
        var request = new CreateBucketRequest { Name = "keyed" };

        var result = await _sut.CreateAsync(request, auth);

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Buckets WHERE Id = @Id",
            p => p.AddWithValue("@Id", result.Id),
            BucketEntity.Read);
        entity!.OwnerKeyPrefix.Should().Be("cf4_aabbccdd");
    }

    // ── ListAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_AdminSeesAll()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(new PaginationParams(), auth);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListAsync_OwnerSeesOnlyOwn()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Owner("alice", "cf4_alice123");

        var result = await _sut.ListAsync(new PaginationParams(), auth);

        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(b => b.Owner.Should().Be("alice"));
    }

    [Fact]
    public async Task ListAsync_ExcludesExpiredByDefault()
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
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "valid00001");
                p.AddWithValue("@Name", "valid");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            });

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(new PaginationParams(), auth, includeExpired: false);

        result.Items.Should().NotContain(b => b.Id == "expired001");
        result.Items.Should().Contain(b => b.Id == "valid00001");
    }

    [Fact]
    public async Task ListAsync_IncludeExpiredShowsAll()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "expired002");
                p.AddWithValue("@Name", "expired-inc");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10));
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1));
            });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "valid00002");
                p.AddWithValue("@Name", "valid-inc");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            });

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(new PaginationParams(), auth, includeExpired: true);

        result.Items.Should().Contain(b => b.Id == "expired002");
        result.Items.Should().Contain(b => b.Id == "valid00002");
    }

    [Fact]
    public async Task ListAsync_RespectsLimitAndOffset()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(
            new PaginationParams { Limit = 1, Offset = 1, Sort = "name", Order = "asc" },
            auth);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(1);
        result.Limit.Should().Be(1);
        result.Offset.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_SortByName()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(
            new PaginationParams { Sort = "name", Order = "asc" },
            auth);

        result.Items.Select(b => b.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListAsync_SortByTotalSize()
    {
        await Db.ExecuteAsync(_db, "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "size00001"); p.AddWithValue("@Name", "small"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@TotalSize", 100L); });
        await Db.ExecuteAsync(_db, "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "size00002"); p.AddWithValue("@Name", "big"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@TotalSize", 10000L); });
        await Db.ExecuteAsync(_db, "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "size00003"); p.AddWithValue("@Name", "medium"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@TotalSize", 1000L); });

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(
            new PaginationParams { Sort = "total_size", Order = "asc" },
            auth);

        result.Items.Where(b => b.Id.StartsWith("size"))
              .Select(b => b.TotalSize)
              .Should().BeInAscendingOrder();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingBucket_ReturnsBucketWithFiles()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @FileCount, @TotalSize)",
            p => { p.AddWithValue("@Id", "get0000001"); p.AddWithValue("@Name", "get-test"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@FileCount", 1); p.AddWithValue("@TotalSize", 512L); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "get0000001"); p.AddWithValue("@Path", "hello.txt"); p.AddWithValue("@Name", "hello.txt"); p.AddWithValue("@Size", 512L); p.AddWithValue("@MimeType", "text/plain"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });

        var result = await _sut.GetByIdAsync("get0000001", includeFiles: true);

        result.Should().NotBeNull();
        result!.Id.Should().Be("get0000001");
        result.Name.Should().Be("get-test");
        result.Files.Should().HaveCount(1);
        result.Files![0].Path.Should().Be("hello.txt");
        result.HasMoreFiles.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ExpiredBucket_ReturnsNull()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "expired010");
                p.AddWithValue("@Name", "expired-get");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10));
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1));
            });

        var result = await _sut.GetByIdAsync("expired010");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_LimitsTo100Files()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, FileCount) VALUES (@Id, @Name, @Owner, @CreatedAt, @FileCount)",
            p => { p.AddWithValue("@Id", "many000001"); p.AddWithValue("@Name", "many-files"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@FileCount", 105); });
        for (int i = 0; i < 105; i++)
        {
            await Db.ExecuteAsync(_db,
                "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
                p => { p.AddWithValue("@BucketId", "many000001"); p.AddWithValue("@Path", $"file{i:D4}.txt"); p.AddWithValue("@Name", $"file{i:D4}.txt"); p.AddWithValue("@Size", 100L); p.AddWithValue("@MimeType", "text/plain"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });
        }

        var result = await _sut.GetByIdAsync("many000001", includeFiles: true);

        result.Should().NotBeNull();
        result!.Files.Should().HaveCount(100);
        result.HasMoreFiles.Should().BeTrue();
    }

    // ── UpdateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesName()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "upd0000001"); p.AddWithValue("@Name", "original"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000001",
            new UpdateBucketRequest { Name = "renamed" }, auth);

        result.Should().NotBeNull();
        result!.Name.Should().Be("renamed");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDescription()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "upd0000002"); p.AddWithValue("@Name", "desc-test"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000002",
            new UpdateBucketRequest { Description = "new desc" }, auth);

        result.Should().NotBeNull();
        result!.Description.Should().Be("new desc");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExpiry()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "upd0000003"); p.AddWithValue("@Name", "exp-test"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(1)); });

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000003",
            new UpdateBucketRequest { ExpiresIn = "never" }, auth);

        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsNull()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("nonexist01",
            new UpdateBucketRequest { Name = "nope" }, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_ReturnsNull()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "upd0000004"); p.AddWithValue("@Name", "not-yours"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.UpdateAsync("upd0000004",
            new UpdateBucketRequest { Name = "stolen" }, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_OwnerCanUpdate()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "upd0000005"); p.AddWithValue("@Name", "mine"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.UpdateAsync("upd0000005",
            new UpdateBucketRequest { Name = "still-mine" }, auth);

        result.Should().NotBeNull();
        result!.Name.Should().Be("still-mine");
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingBucket_DeletesAllRelated()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "del0000001"); p.AddWithValue("@Name", "to-delete"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "del0000001"); p.AddWithValue("@Path", "file.txt"); p.AddWithValue("@Name", "file.txt"); p.AddWithValue("@Size", 100L); p.AddWithValue("@MimeType", "text/plain"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p => { p.AddWithValue("@Code", "abc123"); p.AddWithValue("@BucketId", "del0000001"); p.AddWithValue("@FilePath", "file.txt"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @CreatedAt)",
            p => { p.AddWithValue("@Token", "cfu_testtoken1234567890123456789012345678901234"); p.AddWithValue("@BucketId", "del0000001"); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(1)); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        // Create the bucket directory
        Directory.CreateDirectory(Path.Combine(_tempDir, "del0000001"));

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("del0000001", auth);

        result.Should().BeTrue();

        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM Buckets WHERE Id = 'del0000001'")).Should().Be(0);
        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM Files WHERE BucketId = 'del0000001'")).Should().Be(0);
        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM ShortUrls WHERE BucketId = 'del0000001'")).Should().Be(0);
        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM UploadTokens WHERE BucketId = 'del0000001'")).Should().Be(0);
        Directory.Exists(Path.Combine(_tempDir, "del0000001")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("nonexist02", auth);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ReturnsFalse()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "del0000002"); p.AddWithValue("@Name", "not-yours"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.DeleteAsync("del0000002", auth);

        result.Should().BeFalse();
        // Bucket should still exist
        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM Buckets WHERE Id = 'del0000002'")).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_OwnerCanDelete()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "del0000003"); p.AddWithValue("@Name", "mine-delete"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.DeleteAsync("del0000003", auth);

        result.Should().BeTrue();
        (await Db.ExecuteScalarAsync<int>(_db, "SELECT COUNT(*) FROM Buckets WHERE Id = 'del0000003'")).Should().Be(0);
    }

    // ── GetSummaryAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_ReturnsSummaryText()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt, @FileCount, @TotalSize)",
            p => { p.AddWithValue("@Id", "sum0000001"); p.AddWithValue("@Name", "summary-test"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(30)); p.AddWithValue("@FileCount", 2); p.AddWithValue("@TotalSize", 1536L); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "sum0000001"); p.AddWithValue("@Path", "doc.pdf"); p.AddWithValue("@Name", "doc.pdf"); p.AddWithValue("@Size", 1024L); p.AddWithValue("@MimeType", "application/pdf"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "sum0000001"); p.AddWithValue("@Path", "img.png"); p.AddWithValue("@Name", "img.png"); p.AddWithValue("@Size", 512L); p.AddWithValue("@MimeType", "image/png"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });

        var result = await _sut.GetSummaryAsync("sum0000001");

        result.Should().NotBeNull();
        result.Should().Contain("Bucket: summary-test");
        result.Should().Contain("Owner: admin");
        result.Should().Contain("Files: 2 (1.5 KB)");
        result.Should().Contain("Created: 2025-01-15");
        result.Should().Contain("Expires:");
        result.Should().Contain("doc.pdf (1.0 KB)");
        result.Should().Contain("img.png (512 B)");
    }

    [Fact]
    public async Task GetSummaryAsync_NeverExpiry_ShowsNever()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @FileCount, @TotalSize)",
            p => { p.AddWithValue("@Id", "sum0000002"); p.AddWithValue("@Name", "no-expire-summary"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@FileCount", 0); p.AddWithValue("@TotalSize", 0L); });

        var result = await _sut.GetSummaryAsync("sum0000002");

        result.Should().Contain("Expires: never");
    }

    [Fact]
    public async Task GetSummaryAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetSummaryAsync("nonexist03");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSummaryAsync_ExpiredBucket_ReturnsNull()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p =>
            {
                p.AddWithValue("@Id", "sum0000003");
                p.AddWithValue("@Name", "expired-summary");
                p.AddWithValue("@Owner", "admin");
                p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10));
                p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1));
            });

        var result = await _sut.GetSummaryAsync("sum0000003");
        result.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedBucketsAsync()
    {
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, ExpiresAt, TotalSize) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @ExpiresAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "seed000001"); p.AddWithValue("@Name", "alpha"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@OwnerKeyPrefix", "cf4_alice123"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7)); p.AddWithValue("@TotalSize", 100L); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, ExpiresAt, TotalSize) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @ExpiresAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "seed000002"); p.AddWithValue("@Name", "bravo"); p.AddWithValue("@Owner", "alice"); p.AddWithValue("@OwnerKeyPrefix", "cf4_alice123"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7)); p.AddWithValue("@TotalSize", 200L); });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, ExpiresAt, TotalSize) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @ExpiresAt, @TotalSize)",
            p => { p.AddWithValue("@Id", "seed000003"); p.AddWithValue("@Name", "charlie"); p.AddWithValue("@Owner", "bob"); p.AddWithValue("@OwnerKeyPrefix", "cf4_bob12345"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7)); p.AddWithValue("@TotalSize", 300L); });
    }
}
