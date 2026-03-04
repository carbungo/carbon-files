using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class CleanupServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly DbContextOptions<CarbonFilesDbContext> _dbOptions;
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly CleanupService _sut;

    public CleanupServiceTests()
    {
        // Use a shared named in-memory SQLite database so multiple DbContext instances
        // can share the same data. The _keepAliveConnection keeps the DB alive.
        var dbName = $"CleanupTest_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        _dbOptions = new DbContextOptionsBuilder<CarbonFilesDbContext>()
            .UseSqlite(connectionString)
            .Options;

        // Create schema
        using (var initDb = new CarbonFilesDbContext(_dbOptions))
        {
            initDb.Database.EnsureCreated();
        }

        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cfOptions = Options.Create(new CarbonFilesOptions
        {
            DataDir = _tempDir,
            CleanupIntervalMinutes = 1
        });

        // Build a service provider that the CleanupService can create scopes from.
        // Each scope gets its own CarbonFilesDbContext, but they all share the same
        // in-memory SQLite database via the shared cache connection string.
        var services = new ServiceCollection();
        services.AddDbContext<CarbonFilesDbContext>(opts =>
            opts.UseSqlite(_keepAliveConnection.ConnectionString));
        services.AddSingleton(new FileStorageService(cfOptions, NullLogger<FileStorageService>.Instance));
        services.AddSingleton<ICacheService>(new NullCacheService());
        services.AddScoped<CleanupRepository>();
        _serviceProvider = services.BuildServiceProvider();

        _sut = new CleanupService(
            _serviceProvider,
            cfOptions,
            NullLogger<CleanupService>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private CarbonFilesDbContext CreateDbContext() => new CarbonFilesDbContext(_dbOptions);

    // ── CleanupExpiredBucketsAsync ───────────────────────────────────────

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesExpiredBucket()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000001",
                Name = "expired-bucket",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.FindAsync(new object[] { "exp0000001" }, TestContext.Current.CancellationToken)).Should().BeNull();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveActiveBucket()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "act0000001",
                Name = "active-bucket",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.FindAsync(new object[] { "act0000001" }, TestContext.Current.CancellationToken)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveNeverExpireBucket()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "nev0000001",
                Name = "never-expire",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = null
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.FindAsync(new object[] { "nev0000001" }, TestContext.Current.CancellationToken)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedFiles()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000002",
                Name = "expired-with-files",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1),
                FileCount = 2,
                TotalSize = 200
            });
            db.Files.Add(new FileEntity
            {
                BucketId = "exp0000002",
                Path = "file1.txt",
                Name = "file1.txt",
                Size = 100,
                MimeType = "text/plain",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Files.Add(new FileEntity
            {
                BucketId = "exp0000002",
                Path = "file2.txt",
                Name = "file2.txt",
                Size = 100,
                MimeType = "text/plain",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Files.AnyAsync(f => f.BucketId == "exp0000002", TestContext.Current.CancellationToken)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedShortUrls()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000003",
                Name = "expired-with-urls",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            db.ShortUrls.Add(new ShortUrlEntity
            {
                Code = "abc123",
                BucketId = "exp0000003",
                FilePath = "file.txt",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.ShortUrls.AnyAsync(s => s.BucketId == "exp0000003", TestContext.Current.CancellationToken)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedUploadTokens()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000004",
                Name = "expired-with-tokens",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            db.UploadTokens.Add(new UploadTokenEntity
            {
                Token = "cfu_testtoken1234567890123456789012345678901234",
                BucketId = "exp0000004",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.UploadTokens.AnyAsync(t => t.BucketId == "exp0000004", TestContext.Current.CancellationToken)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DeletesBucketDirectory()
    {
        var bucketDir = Path.Combine(_tempDir, "exp0000005");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "test.txt"), "hello");

        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000005",
                Name = "expired-with-dir",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        Directory.Exists(bucketDir).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_NoExpiredBuckets_DoesNothing()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "act0000002",
                Name = "still-active",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_OnlyRemovesExpired_LeavesActive()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000006",
                Name = "expired",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            db.Buckets.Add(new BucketEntity
            {
                Id = "act0000003",
                Name = "active",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.FindAsync(new object[] { "exp0000006" }, TestContext.Current.CancellationToken)).Should().BeNull();
            (await db.Buckets.FindAsync(new object[] { "act0000003" }, TestContext.Current.CancellationToken)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesMultipleExpiredBuckets()
    {
        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000007",
                Name = "expired-1",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-2)
            });
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000008",
                Name = "expired-2",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            (await db.Buckets.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        }
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_FullCleanup_RemovesAllAssociatedData()
    {
        // Create an expired bucket with files, short URLs, upload tokens, and a directory
        var bucketDir = Path.Combine(_tempDir, "exp0000009");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "data.bin"), "test data");

        using (var db = CreateDbContext())
        {
            db.Buckets.Add(new BucketEntity
            {
                Id = "exp0000009",
                Name = "full-cleanup",
                Owner = "admin",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-1),
                FileCount = 1,
                TotalSize = 100
            });
            db.Files.Add(new FileEntity
            {
                BucketId = "exp0000009",
                Path = "data.bin",
                Name = "data.bin",
                Size = 100,
                MimeType = "application/octet-stream",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.ShortUrls.Add(new ShortUrlEntity
            {
                Code = "xyz789",
                BucketId = "exp0000009",
                FilePath = "data.bin",
                CreatedAt = DateTime.UtcNow
            });
            db.UploadTokens.Add(new UploadTokenEntity
            {
                Token = "cfu_fullcleanup12345678901234567890123456789012",
                BucketId = "exp0000009",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        using (var db = CreateDbContext())
        {
            // All database records should be gone
            (await db.Buckets.FindAsync(new object[] { "exp0000009" }, TestContext.Current.CancellationToken)).Should().BeNull();
            (await db.Files.AnyAsync(f => f.BucketId == "exp0000009", TestContext.Current.CancellationToken)).Should().BeFalse();
            (await db.ShortUrls.AnyAsync(s => s.BucketId == "exp0000009", TestContext.Current.CancellationToken)).Should().BeFalse();
            (await db.UploadTokens.AnyAsync(t => t.BucketId == "exp0000009", TestContext.Current.CancellationToken)).Should().BeFalse();
        }

        // Directory should be gone
        Directory.Exists(bucketDir).Should().BeFalse();
    }
}
