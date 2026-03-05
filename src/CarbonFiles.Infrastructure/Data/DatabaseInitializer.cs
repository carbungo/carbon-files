using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Data;

/// <summary>
/// Initializes the SQLite database schema using raw SQL.
/// Both Migrate() and EnsureCreated() rely on design-time operations
/// that are trimmed under Native AOT. This class creates tables directly.
/// </summary>
public static class DatabaseInitializer
{
    internal const string Schema = """
        CREATE TABLE IF NOT EXISTS "Buckets" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Buckets" PRIMARY KEY,
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

        CREATE INDEX IF NOT EXISTS "IX_Buckets_OwnerKeyPrefix" ON "Buckets" ("OwnerKeyPrefix");
        CREATE INDEX IF NOT EXISTS "IX_Buckets_ExpiresAt" ON "Buckets" ("ExpiresAt");
        CREATE INDEX IF NOT EXISTS "IX_Buckets_Owner" ON "Buckets" ("Owner");

        CREATE TABLE IF NOT EXISTS "ContentObjects" (
            "Hash" TEXT NOT NULL CONSTRAINT "PK_ContentObjects" PRIMARY KEY,
            "Size" INTEGER NOT NULL,
            "DiskPath" TEXT NOT NULL,
            "RefCount" INTEGER NOT NULL DEFAULT 1,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_ContentObjects_Orphans"
            ON "ContentObjects" ("RefCount") WHERE "RefCount" = 0;

        CREATE TABLE IF NOT EXISTS "Files" (
            "BucketId" TEXT NOT NULL,
            "Path" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Size" INTEGER NOT NULL DEFAULT 0,
            "MimeType" TEXT NOT NULL,
            "ShortCode" TEXT NULL,
            "ContentHash" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            CONSTRAINT "PK_Files" PRIMARY KEY ("BucketId", "Path")
        );

        CREATE INDEX IF NOT EXISTS "IX_Files_BucketId" ON "Files" ("BucketId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Files_ShortCode" ON "Files" ("ShortCode") WHERE "ShortCode" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS "IX_Files_ContentHash" ON "Files" ("ContentHash");

        CREATE TABLE IF NOT EXISTS "ApiKeys" (
            "Prefix" TEXT NOT NULL CONSTRAINT "PK_ApiKeys" PRIMARY KEY,
            "HashedSecret" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "LastUsedAt" TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS "ShortUrls" (
            "Code" TEXT NOT NULL CONSTRAINT "PK_ShortUrls" PRIMARY KEY,
            "BucketId" TEXT NOT NULL,
            "FilePath" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_ShortUrls_BucketId_FilePath" ON "ShortUrls" ("BucketId", "FilePath");

        CREATE TABLE IF NOT EXISTS "UploadTokens" (
            "Token" TEXT NOT NULL CONSTRAINT "PK_UploadTokens" PRIMARY KEY,
            "BucketId" TEXT NOT NULL,
            "ExpiresAt" TEXT NOT NULL,
            "MaxUploads" INTEGER NULL,
            "UploadsUsed" INTEGER NOT NULL DEFAULT 0,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_UploadTokens_BucketId" ON "UploadTokens" ("BucketId");
        """;

    public static void Initialize(IDbConnection db, ILogger? logger = null)
    {
        var sqlite = (SqliteConnection)db;

        // WAL mode + resilience PRAGMAs
        using var pragmaCmd = sqlite.CreateCommand();
        pragmaCmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA wal_autocheckpoint=1000;
            """;
        pragmaCmd.ExecuteNonQuery();

        // Schema
        using var schemaCmd = sqlite.CreateCommand();
        schemaCmd.CommandText = Schema;
        schemaCmd.ExecuteNonQuery();

        // Integrity check
        RunIntegrityCheck(sqlite, logger);
    }

    internal static bool RunIntegrityCheck(SqliteConnection sqlite, ILogger? logger)
    {
        using var checkCmd = sqlite.CreateCommand();
        checkCmd.CommandText = "PRAGMA quick_check;";
        var result = checkCmd.ExecuteScalar()?.ToString();

        if (result == "ok")
        {
            logger?.LogInformation("Database integrity check passed");
            return true;
        }

        logger?.LogWarning("Database integrity check failed: {Result}. Attempting REINDEX", result);
        try
        {
            using var reindexCmd = sqlite.CreateCommand();
            reindexCmd.CommandText = "REINDEX;";
            reindexCmd.ExecuteNonQuery();
            logger?.LogInformation("REINDEX completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "REINDEX failed — database may have corruption. Manual intervention may be required");
            return false;
        }
    }
}
