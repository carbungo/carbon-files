using System.Security.Cryptography;
using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;

var dbPath = Environment.GetEnvironmentVariable("CarbonFiles__DbPath")
    ?? args.FirstOrDefault()
    ?? "./data/carbonfiles.db";

var dataDir = Environment.GetEnvironmentVariable("CarbonFiles__DataDir")
    ?? Path.GetDirectoryName(Path.GetFullPath(dbPath))
    ?? "./data";

var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dir != null) Directory.CreateDirectory(dir);

Console.WriteLine($"Initializing database: {dbPath}");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
DatabaseInitializer.Initialize(conn);

Console.WriteLine("Database initialization complete.");

// --- CAS Migration ---
// Add ContentHash column if missing (existing databases)
try
{
    using var alterCmd = conn.CreateCommand();
    alterCmd.CommandText = "ALTER TABLE Files ADD COLUMN \"ContentHash\" TEXT NULL";
    alterCmd.ExecuteNonQuery();
    Console.WriteLine("Added ContentHash column to Files table.");
}
catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
{
    // Column already exists — fine
}

// Migrate unmigrated files to CAS
var contentDir = Path.Combine(dataDir, "content");

using var countCmd = conn.CreateCommand();
countCmd.CommandText = "SELECT COUNT(*) FROM Files WHERE ContentHash IS NULL";
var unmigratedCount = Convert.ToInt32(countCmd.ExecuteScalar());

if (unmigratedCount == 0)
{
    Console.WriteLine("No files to migrate to CAS.");
    return;
}

Console.WriteLine($"Migrating {unmigratedCount} files to content-addressable storage...");

using var selectCmd = conn.CreateCommand();
selectCmd.CommandText = "SELECT BucketId, Path, Size FROM Files WHERE ContentHash IS NULL";
using var reader = selectCmd.ExecuteReader();

var migrated = 0;
var skipped = 0;
var deduped = 0;

while (reader.Read())
{
    var bucketId = reader.GetString(0);
    var filePath = reader.GetString(1);
    var size = reader.GetInt64(2);

    var encodedPath = Uri.EscapeDataString(filePath);
    var oldPath = Path.Combine(dataDir, bucketId, encodedPath);

    if (!File.Exists(oldPath))
    {
        Console.WriteLine($"  SKIP: {bucketId}/{filePath} (file not found on disk)");
        skipped++;
        continue;
    }

    // Compute SHA256
    string hash;
    using (var stream = File.OpenRead(oldPath))
    using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
    {
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            sha256.AppendData(buffer, 0, bytesRead);
        hash = Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    var diskPath = Path.Combine(hash[..2], hash[2..4], hash);
    var fullContentPath = Path.Combine(contentDir, diskPath);

    // Check if content already exists in CAS
    using var checkCmd = conn.CreateCommand();
    checkCmd.CommandText = "SELECT RefCount FROM ContentObjects WHERE Hash = @hash";
    checkCmd.Parameters.AddWithValue("@hash", hash);
    var existingRef = checkCmd.ExecuteScalar();

    using var tx = conn.BeginTransaction();

    if (existingRef != null)
    {
        // Dedup: increment ref count, remove old file
        using var incrCmd = conn.CreateCommand();
        incrCmd.Transaction = tx;
        incrCmd.CommandText = "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash";
        incrCmd.Parameters.AddWithValue("@hash", hash);
        incrCmd.ExecuteNonQuery();

        File.Delete(oldPath);
        deduped++;
    }
    else
    {
        // New content: move to CAS store
        var contentFileDir = Path.GetDirectoryName(fullContentPath)!;
        Directory.CreateDirectory(contentFileDir);
        File.Move(oldPath, fullContentPath);

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)";
        insertCmd.Parameters.AddWithValue("@hash", hash);
        insertCmd.Parameters.AddWithValue("@size", size);
        insertCmd.Parameters.AddWithValue("@diskPath", diskPath);
        insertCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        insertCmd.ExecuteNonQuery();
    }

    // Update file record with content hash
    using var updateCmd = conn.CreateCommand();
    updateCmd.Transaction = tx;
    updateCmd.CommandText = "UPDATE Files SET ContentHash = @hash WHERE BucketId = @bucketId AND Path = @path";
    updateCmd.Parameters.AddWithValue("@hash", hash);
    updateCmd.Parameters.AddWithValue("@bucketId", bucketId);
    updateCmd.Parameters.AddWithValue("@path", filePath);
    updateCmd.ExecuteNonQuery();

    tx.Commit();
    migrated++;

    if (migrated % 100 == 0)
        Console.WriteLine($"  Progress: {migrated}/{unmigratedCount} migrated, {deduped} deduplicated");
}

Console.WriteLine($"CAS migration complete: {migrated} migrated, {deduped} deduplicated, {skipped} skipped.");
