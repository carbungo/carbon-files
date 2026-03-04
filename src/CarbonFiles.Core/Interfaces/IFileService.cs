using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IFileService
{
    Task<PaginatedResponse<BucketFile>> ListAsync(string bucketId, PaginationParams pagination);
    Task<DirectoryListingResponse> ListDirectoryAsync(string bucketId, string path, PaginationParams pagination);
    Task<FileTreeResponse> ListTreeAsync(string bucketId, string? prefix, string delimiter, int limit, string? cursor);
    Task<BucketFile?> GetMetadataAsync(string bucketId, string path);
    Task<string?> GetContentDiskPathAsync(string bucketId, string path);
    Task<bool> DeleteAsync(string bucketId, string path, AuthContext auth);
    Task UpdateLastUsedAsync(string bucketId);
    Task<bool> UpdateFileSizeAsync(string bucketId, string path, long newSize);
    Task<bool> PatchFileAsync(string bucketId, string path, Stream content, long offset, bool append);
}
