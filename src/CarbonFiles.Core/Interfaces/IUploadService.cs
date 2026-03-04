using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Interfaces;

public interface IUploadService
{
    Task<BucketFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default);
    Task<long> GetStoredFileSizeAsync(string bucketId, string path);
}
