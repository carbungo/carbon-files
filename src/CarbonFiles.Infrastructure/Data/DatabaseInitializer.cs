using System.Data;
using Microsoft.Data.Sqlite;

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

        CREATE TABLE IF NOT EXISTS "Files" (
            "BucketId" TEXT NOT NULL,
            "Path" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Size" INTEGER NOT NULL DEFAULT 0,
            "MimeType" TEXT NOT NULL,
            "ShortCode" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            CONSTRAINT "PK_Files" PRIMARY KEY ("BucketId", "Path")
        );

        CREATE INDEX IF NOT EXISTS "IX_Files_BucketId" ON "Files" ("BucketId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Files_ShortCode" ON "Files" ("ShortCode") WHERE "ShortCode" IS NOT NULL;

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

    public static void Initialize(IDbConnection db)
    {
        var sqlite = (SqliteConnection)db;

        using var pragmaCmd = sqlite.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL;";
        pragmaCmd.ExecuteNonQuery();

        using var schemaCmd = sqlite.CreateCommand();
        schemaCmd.CommandText = Schema;
        schemaCmd.ExecuteNonQuery();
    }
}
