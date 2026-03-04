using System.Data;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class CleanupServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly string _connectionString;
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly CleanupService _sut;

    public CleanupServiceTests()
    {
        // Use a shared named in-memory SQLite database so multiple connections
        // can share the same data. The _keepAliveConnection keeps the DB alive.
        var dbName = $"CleanupTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        // Create schema
        DatabaseInitializer.Initialize(_keepAliveConnection);

        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cfOptions = Options.Create(new CarbonFilesOptions
        {
            DataDir = _tempDir,
            CleanupIntervalMinutes = 1
        });

        // Build a service provider that the CleanupService can create scopes from.
        var services = new ServiceCollection();
        services.AddScoped<IDbConnection>(_ =>
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        });
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

    // ── CleanupExpiredBucketsAsync ───────────────────────────────────────

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesExpiredBucket()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000001"); p.AddWithValue("@Name", "expired-bucket"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000001'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveActiveBucket()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "act0000001"); p.AddWithValue("@Name", "active-bucket"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'act0000001'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveNeverExpireBucket()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            p => { p.AddWithValue("@Id", "nev0000001"); p.AddWithValue("@Name", "never-expire"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'nev0000001'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedFiles()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt, @FileCount, @TotalSize)",
            p => { p.AddWithValue("@Id", "exp0000002"); p.AddWithValue("@Name", "expired-with-files"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); p.AddWithValue("@FileCount", 2); p.AddWithValue("@TotalSize", 200L); });
        Execute("INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "exp0000002"); p.AddWithValue("@Path", "file1.txt"); p.AddWithValue("@Name", "file1.txt"); p.AddWithValue("@Size", 100L); p.AddWithValue("@MimeType", "text/plain"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });
        Execute("INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "exp0000002"); p.AddWithValue("@Path", "file2.txt"); p.AddWithValue("@Name", "file2.txt"); p.AddWithValue("@Size", 100L); p.AddWithValue("@MimeType", "text/plain"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Files WHERE BucketId = 'exp0000002'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedShortUrls()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000003"); p.AddWithValue("@Name", "expired-with-urls"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });
        Execute("INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p => { p.AddWithValue("@Code", "abc123"); p.AddWithValue("@BucketId", "exp0000003"); p.AddWithValue("@FilePath", "file.txt"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM ShortUrls WHERE BucketId = 'exp0000003'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedUploadTokens()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000004"); p.AddWithValue("@Name", "expired-with-tokens"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });
        Execute("INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @CreatedAt)",
            p => { p.AddWithValue("@Token", "cfu_testtoken1234567890123456789012345678901234"); p.AddWithValue("@BucketId", "exp0000004"); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(1)); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM UploadTokens WHERE BucketId = 'exp0000004'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DeletesBucketDirectory()
    {
        var bucketDir = Path.Combine(_tempDir, "exp0000005");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "test.txt"), "hello");

        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000005"); p.AddWithValue("@Name", "expired-with-dir"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        Directory.Exists(bucketDir).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_NoExpiredBuckets_DoesNothing()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "act0000002"); p.AddWithValue("@Name", "still-active"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(30)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_OnlyRemovesExpired_LeavesActive()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000006"); p.AddWithValue("@Name", "expired"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "act0000003"); p.AddWithValue("@Name", "active"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000006'").Should().Be(0);
        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'act0000003'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesMultipleExpiredBuckets()
    {
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000007"); p.AddWithValue("@Name", "expired-1"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-2)); });
        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            p => { p.AddWithValue("@Id", "exp0000008"); p.AddWithValue("@Name", "expired-2"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-5)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        ScalarInt("SELECT COUNT(*) FROM Buckets").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_FullCleanup_RemovesAllAssociatedData()
    {
        // Create an expired bucket with files, short URLs, upload tokens, and a directory
        var bucketDir = Path.Combine(_tempDir, "exp0000009");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "data.bin"), "test data");

        Execute("INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt, @FileCount, @TotalSize)",
            p => { p.AddWithValue("@Id", "exp0000009"); p.AddWithValue("@Name", "full-cleanup"); p.AddWithValue("@Owner", "admin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow.AddDays(-10)); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(-1)); p.AddWithValue("@FileCount", 1); p.AddWithValue("@TotalSize", 100L); });
        Execute("INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            p => { p.AddWithValue("@BucketId", "exp0000009"); p.AddWithValue("@Path", "data.bin"); p.AddWithValue("@Name", "data.bin"); p.AddWithValue("@Size", 100L); p.AddWithValue("@MimeType", "application/octet-stream"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); p.AddWithValue("@UpdatedAt", DateTime.UtcNow); });
        Execute("INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            p => { p.AddWithValue("@Code", "xyz789"); p.AddWithValue("@BucketId", "exp0000009"); p.AddWithValue("@FilePath", "data.bin"); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });
        Execute("INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @CreatedAt)",
            p => { p.AddWithValue("@Token", "cfu_fullcleanup12345678901234567890123456789012"); p.AddWithValue("@BucketId", "exp0000009"); p.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(1)); p.AddWithValue("@CreatedAt", DateTime.UtcNow); });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        // All database records should be gone
        ScalarInt("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000009'").Should().Be(0);
        ScalarInt("SELECT COUNT(*) FROM Files WHERE BucketId = 'exp0000009'").Should().Be(0);
        ScalarInt("SELECT COUNT(*) FROM ShortUrls WHERE BucketId = 'exp0000009'").Should().Be(0);
        ScalarInt("SELECT COUNT(*) FROM UploadTokens WHERE BucketId = 'exp0000009'").Should().Be(0);

        // Directory should be gone
        Directory.Exists(bucketDir).Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void Execute(string sql, Action<SqliteParameterCollection> parameters)
    {
        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = sql;
        parameters(cmd.Parameters);
        cmd.ExecuteNonQuery();
    }

    private int ScalarInt(string sql)
    {
        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
