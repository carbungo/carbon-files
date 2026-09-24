using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CarbonFiles.Api.Tests;

public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseInitializerTests()
    {
        // Use a temp file DB — in-memory SQLite doesn't support WAL
        var dbPath = Path.Combine(Path.GetTempPath(), $"cf_pragma_test_{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        DatabaseInitializer.Initialize(_connection);
    }

    [Fact]
    public void Initialize_SetsWalJournalMode()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("wal", result);
    }

    [Fact]
    public void Initialize_SetsSynchronousNormal()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        // synchronous=NORMAL is 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void Initialize_SetsWalAutocheckpoint()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_autocheckpoint;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1000, result);
    }

    [Fact]
    public void Initialize_IntegrityCheckPasses()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("ok", result);
    }

    [Fact]
    public void Initialize_AddsSpaModeColumnToExistingDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cf_upgrade_test_{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using (var create = connection.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE "Buckets" (
                        "Id" TEXT NOT NULL PRIMARY KEY,
                        "Name" TEXT NOT NULL,
                        "Owner" TEXT NOT NULL,
                        "OwnerKeyPrefix" TEXT NULL,
                        "Description" TEXT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        "ExpiresAt" TEXT NULL,
                        "LastUsedAt" TEXT NULL,
                        "FileCount" INTEGER NOT NULL DEFAULT 0,
                        "TotalSize" INTEGER NOT NULL DEFAULT 0,
                        "DownloadCount" INTEGER NOT NULL DEFAULT 0
                    );
                    """;
                create.ExecuteNonQuery();
            }

            DatabaseInitializer.Initialize(connection);

            using var columns = connection.CreateCommand();
            columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Buckets') WHERE name = 'SpaMode';";
            Assert.Equal(1L, (long)columns.ExecuteScalar()!);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
        }
    }

    public void Dispose()
    {
        var dbPath = _connection.DataSource;
        _connection.Dispose();
        // Clean up temp DB files
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }
}
