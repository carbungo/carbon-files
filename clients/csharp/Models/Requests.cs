using System.Text.Json.Serialization;

namespace CarbonFiles.Client.Models;

public class CreateBucketRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("expires_in")]
    public string? ExpiresIn { get; set; }
}

public class UpdateBucketRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("expires_in")]
    public string? ExpiresIn { get; set; }
}

public class CreateApiKeyRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class CreateUploadTokenRequest
{
    [JsonPropertyName("expires_in")]
    public string? ExpiresIn { get; set; }

    [JsonPropertyName("max_uploads")]
    public int? MaxUploads { get; set; }
}

public class CreateDashboardTokenRequest
{
    [JsonPropertyName("expires_in")]
    public string? ExpiresIn { get; set; }
}

public class SyncDeployRequest
{
    [JsonPropertyName("files")]
    public List<DeployFile> Files { get; set; } = new();
}

public class DeployFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}
