using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IFileService
{
    Task<PaginatedResponse<BucketFile>> ListAsync(string bucketId, PaginationParams pagination);
    Task<DirectoryListingResponse> ListDirectoryAsync(string bucketId, string path, PaginationParams pagination);
    Task<BucketFile?> GetMetadataAsync(string bucketId, string path);
    Task<bool> DeleteAsync(string bucketId, string path, AuthContext auth);
    Task UpdateLastUsedAsync(string bucketId);
    Task<bool> UpdateFileSizeAsync(string bucketId, string path, long newSize);
}
