using System.Text.Json.Serialization;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Serialization;

[JsonSerializable(typeof(Bucket))]
[JsonSerializable(typeof(BucketFile))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(PaginatedResponse<Bucket>))]
[JsonSerializable(typeof(PaginatedResponse<BucketFile>))]
[JsonSerializable(typeof(PaginatedResponse<ApiKeyListItem>))]
[JsonSerializable(typeof(BucketDetailResponse))]
[JsonSerializable(typeof(DirectoryListingResponse))]
[JsonSerializable(typeof(CreateBucketRequest))]
[JsonSerializable(typeof(UpdateBucketRequest))]
[JsonSerializable(typeof(CreateApiKeyRequest))]
[JsonSerializable(typeof(CreateDashboardTokenRequest))]
[JsonSerializable(typeof(CreateUploadTokenRequest))]
[JsonSerializable(typeof(ApiKeyResponse))]
[JsonSerializable(typeof(ApiKeyListItem))]
[JsonSerializable(typeof(ApiKeyUsageResponse))]
[JsonSerializable(typeof(DashboardTokenResponse))]
[JsonSerializable(typeof(DashboardTokenInfo))]
[JsonSerializable(typeof(UploadTokenResponse))]
[JsonSerializable(typeof(UploadResponse))]
[JsonSerializable(typeof(UploadedFile))]
[JsonSerializable(typeof(FileTreeResponse))]
[JsonSerializable(typeof(DirectoryEntry))]
[JsonSerializable(typeof(VerifyResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(OwnerStats))]
[JsonSerializable(typeof(BucketChanges))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CarbonFilesJsonContext : JsonSerializerContext { }
