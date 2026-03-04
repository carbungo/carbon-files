using System.Security.Cryptography;
using System.Text;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class ApiKeyServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ApiKeyService _sut;

    public ApiKeyServiceTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseInitializer.Initialize(_db);

        _sut = new ApiKeyService(_db, NullLogger<ApiKeyService>.Instance);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsFullKey()
    {
        var result = await _sut.CreateAsync("my-agent");

        result.Key.Should().StartWith("cf4_");
        result.Key.Split('_').Should().HaveCount(3);
        result.Prefix.Should().StartWith("cf4_");
        result.Name.Should().Be("my-agent");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_StoresHashedSecret_NotPlaintext()
    {
        var result = await _sut.CreateAsync("hashed-test");

        // Extract secret from the full key
        var secret = result.Key[(result.Prefix.Length + 1)..];

        // Verify the stored entity has hashed secret, not plaintext
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @Prefix",
            p => p.AddWithValue("@Prefix", result.Prefix),
            ApiKeyEntity.Read);
        entity.Should().NotBeNull();
        entity!.HashedSecret.Should().NotBe(secret);

        // Verify it IS the SHA-256 hash of the secret
        var expectedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        entity.HashedSecret.Should().Be(expectedHash);
    }

    [Fact]
    public async Task CreateAsync_StoresEntityInDatabase()
    {
        var result = await _sut.CreateAsync("stored-test");

        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @Prefix",
            p => p.AddWithValue("@Prefix", result.Prefix),
            ApiKeyEntity.Read);
        entity.Should().NotBeNull();
        entity!.Name.Should().Be("stored-test");
        entity.Prefix.Should().Be(result.Prefix);
    }

    // ── ValidateKeyAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ValidateKeyAsync_CorrectKey_ReturnsNameAndPrefix()
    {
        var created = await _sut.CreateAsync("validate-test");

        var result = await _sut.ValidateKeyAsync(created.Key);

        result.Should().NotBeNull();
        result!.Value.Name.Should().Be("validate-test");
        result!.Value.Prefix.Should().Be(created.Prefix);
    }

    [Fact]
    public async Task ValidateKeyAsync_WrongSecret_ReturnsNull()
    {
        var created = await _sut.CreateAsync("wrong-secret-test");

        // Modify the secret part
        var wrongKey = created.Prefix + "_00000000000000000000000000000000";
        var result = await _sut.ValidateKeyAsync(wrongKey);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateKeyAsync_NonexistentPrefix_ReturnsNull()
    {
        var result = await _sut.ValidateKeyAsync("cf4_deadbeef_00000000000000000000000000000000");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateKeyAsync_InvalidFormat_ReturnsNull()
    {
        var result = await _sut.ValidateKeyAsync("not-a-valid-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateKeyAsync_WrongProtocol_ReturnsNull()
    {
        var result = await _sut.ValidateKeyAsync("cf3_deadbeef_00000000000000000000000000000000");

        result.Should().BeNull();
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingKey_ReturnsTrueAndRemoves()
    {
        var created = await _sut.CreateAsync("delete-test");

        var deleted = await _sut.DeleteAsync(created.Prefix);

        deleted.Should().BeTrue();
        var entity = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ApiKeys WHERE Prefix = @Prefix",
            p => p.AddWithValue("@Prefix", created.Prefix),
            ApiKeyEntity.Read);
        entity.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonexistentKey_ReturnsFalse()
    {
        var deleted = await _sut.DeleteAsync("cf4_nonexist");

        deleted.Should().BeFalse();
    }

    // ── ListAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsCorrectCount()
    {
        await _sut.CreateAsync("list-1");
        await _sut.CreateAsync("list-2");
        await _sut.CreateAsync("list-3");

        var result = await _sut.ListAsync(new PaginationParams { Limit = 50, Offset = 0 });

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListAsync_RespectsLimitAndOffset()
    {
        await _sut.CreateAsync("page-1");
        await _sut.CreateAsync("page-2");
        await _sut.CreateAsync("page-3");

        var result = await _sut.ListAsync(new PaginationParams { Limit = 1, Offset = 1, Sort = "created_at", Order = "asc" });

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("page-2");
        result.Limit.Should().Be(1);
        result.Offset.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_DoesNotReturnSecret()
    {
        var created = await _sut.CreateAsync("no-secret-list");
        var secret = created.Key[(created.Prefix.Length + 1)..];

        var result = await _sut.ListAsync(new PaginationParams());

        // ApiKeyListItem has no Key or Secret property — verify by checking the item
        var item = result.Items.First(i => i.Prefix == created.Prefix);
        item.Prefix.Should().Be(created.Prefix);
        item.Name.Should().Be("no-secret-list");
        // The type ApiKeyListItem has no Key/Secret field, so secret cannot be exposed
    }

    [Fact]
    public async Task ListAsync_IncludesBucketStats()
    {
        var created = await _sut.CreateAsync("stats-test");

        // Seed some buckets associated with this key
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @FileCount, @TotalSize)",
            p =>
            {
                p.AddWithValue("@Id", "bkt001");
                p.AddWithValue("@Name", "bucket-1");
                p.AddWithValue("@Owner", "stats-test");
                p.AddWithValue("@OwnerKeyPrefix", created.Prefix);
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@FileCount", 5);
                p.AddWithValue("@TotalSize", 1024L);
            });
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @FileCount, @TotalSize)",
            p =>
            {
                p.AddWithValue("@Id", "bkt002");
                p.AddWithValue("@Name", "bucket-2");
                p.AddWithValue("@Owner", "stats-test");
                p.AddWithValue("@OwnerKeyPrefix", created.Prefix);
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@FileCount", 3);
                p.AddWithValue("@TotalSize", 2048L);
            });

        var result = await _sut.ListAsync(new PaginationParams());
        var item = result.Items.First(i => i.Prefix == created.Prefix);

        item.BucketCount.Should().Be(2);
        item.FileCount.Should().Be(8);
        item.TotalSize.Should().Be(3072);
    }

    [Fact]
    public async Task ListAsync_SortByName()
    {
        await _sut.CreateAsync("charlie");
        await _sut.CreateAsync("alpha");
        await _sut.CreateAsync("bravo");

        var result = await _sut.ListAsync(new PaginationParams { Sort = "name", Order = "asc" });

        result.Items.Select(i => i.Name).Should().BeInAscendingOrder();
    }

    // ── GetUsageAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUsageAsync_ExistingKey_ReturnsUsage()
    {
        var created = await _sut.CreateAsync("usage-test");

        // Add a bucket
        await Db.ExecuteAsync(_db,
            "INSERT INTO Buckets (Id, Name, Owner, OwnerKeyPrefix, CreatedAt, FileCount, TotalSize, DownloadCount) VALUES (@Id, @Name, @Owner, @OwnerKeyPrefix, @CreatedAt, @FileCount, @TotalSize, @DownloadCount)",
            p =>
            {
                p.AddWithValue("@Id", "ubkt01");
                p.AddWithValue("@Name", "usage-bucket");
                p.AddWithValue("@Owner", "usage-test");
                p.AddWithValue("@OwnerKeyPrefix", created.Prefix);
                p.AddWithValue("@CreatedAt", DateTime.UtcNow);
                p.AddWithValue("@FileCount", 10);
                p.AddWithValue("@TotalSize", 5000L);
                p.AddWithValue("@DownloadCount", 42L);
            });

        var result = await _sut.GetUsageAsync(created.Prefix);

        result.Should().NotBeNull();
        result!.Prefix.Should().Be(created.Prefix);
        result.Name.Should().Be("usage-test");
        result.BucketCount.Should().Be(1);
        result.FileCount.Should().Be(10);
        result.TotalSize.Should().Be(5000);
        result.TotalDownloads.Should().Be(42);
        result.Buckets.Should().HaveCount(1);
        result.Buckets[0].Name.Should().Be("usage-bucket");
    }

    [Fact]
    public async Task GetUsageAsync_NonexistentKey_ReturnsNull()
    {
        var result = await _sut.GetUsageAsync("cf4_nonexist");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUsageAsync_KeyWithNoBuckets_ReturnsZeroStats()
    {
        var created = await _sut.CreateAsync("empty-usage");

        var result = await _sut.GetUsageAsync(created.Prefix);

        result.Should().NotBeNull();
        result!.BucketCount.Should().Be(0);
        result.FileCount.Should().Be(0);
        result.TotalSize.Should().Be(0);
        result.TotalDownloads.Should().Be(0);
        result.Buckets.Should().BeEmpty();
    }
}
