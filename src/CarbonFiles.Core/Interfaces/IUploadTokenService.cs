using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IUploadTokenService
{
    Task<UploadTokenResponse?> CreateAsync(string bucketId, CreateUploadTokenRequest request, AuthContext auth);
    Task<(string BucketId, bool IsValid)> ValidateAsync(string token);
    Task IncrementUsageAsync(string token, int count);
}
