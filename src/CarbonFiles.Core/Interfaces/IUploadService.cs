using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IUploadService
{
    Task<UploadedFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default);
}
