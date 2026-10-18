using System.Text.Json.Serialization;

namespace CarbonFiles.Client.Models;

public class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; set; }

    [JsonPropertyName("db")]
    public string Db { get; set; } = "";
}

public class UploadedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = "";

    [JsonPropertyName("short_code")]
    public string? ShortCode { get; set; }

    [JsonPropertyName("short_url")]
    public string? ShortUrl { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("deduplicated")]
    public bool Deduplicated { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UploadResponse
{
    [JsonPropertyName("uploaded")]
    public List<UploadedFile> Uploaded { get; set; } = new();
}

public class BucketDetailResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("unique_content_count")]
    public int UniqueContentCount { get; set; }

    [JsonPropertyName("unique_content_size")]
    public long UniqueContentSize { get; set; }

    [JsonPropertyName("files")]
    public List<BucketFile> Files { get; set; } = new();

    [JsonPropertyName("has_more_files")]
    public bool HasMoreFiles { get; set; }
}

public class DirectoryListingResponse
{
    [JsonPropertyName("files")]
    public List<BucketFile> Files { get; set; } = new();

    [JsonPropertyName("folders")]
    public List<string> Folders { get; set; } = new();

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("total_folders")]
    public int TotalFolders { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}

public class FileTreeResponse
{
    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("delimiter")]
    public string Delimiter { get; set; } = "";

    [JsonPropertyName("directories")]
    public List<DirectoryEntry> Directories { get; set; } = new();

    [JsonPropertyName("files")]
    public List<BucketFile> Files { get; set; } = new();

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("total_directories")]
    public int TotalDirectories { get; set; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

public class DirectoryEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }
}

public class VerifyResponse
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("stored_hash")]
    public string StoredHash { get; set; } = "";

    [JsonPropertyName("computed_hash")]
    public string ComputedHash { get; set; } = "";

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }
}

public class ApiKeyResponse
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

public class ApiKeyListItem
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; set; }

    [JsonPropertyName("bucket_count")]
    public int BucketCount { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }
}

public class ApiKeyUsageResponse
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; set; }

    [JsonPropertyName("bucket_count")]
    public int BucketCount { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("total_downloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("buckets")]
    public List<Bucket> Buckets { get; set; } = new();
}

public class UploadTokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("bucket_id")]
    public string BucketId { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("max_uploads")]
    public int? MaxUploads { get; set; }

    [JsonPropertyName("uploads_used")]
    public int UploadsUsed { get; set; }
}

public class DashboardTokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public class DashboardTokenInfo
{
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public class StatsResponse
{
    [JsonPropertyName("total_buckets")]
    public int TotalBuckets { get; set; }

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("total_keys")]
    public int TotalKeys { get; set; }

    [JsonPropertyName("total_downloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("storage_by_owner")]
    public List<OwnerStats> StorageByOwner { get; set; } = new();
}

public class OwnerStats
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("bucket_count")]
    public int BucketCount { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }
}

public class BucketChanges
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class SyncDeployResponse
{
    [JsonPropertyName("kept")]
    public int Kept { get; set; }

    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }
}
