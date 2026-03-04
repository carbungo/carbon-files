using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IBucketService
{
    Task<Bucket> CreateAsync(CreateBucketRequest request, AuthContext auth);
    Task<PaginatedResponse<Bucket>> ListAsync(PaginationParams pagination, AuthContext auth, bool includeExpired = false);
    Task<BucketDetailResponse?> GetByIdAsync(string id, bool includeFiles = false);
    Task<Bucket?> GetBucketAsync(string id);
    Task<List<BucketFile>> GetAllFilesAsync(string id, CancellationToken ct = default);
    Task<Bucket?> UpdateAsync(string id, UpdateBucketRequest request, AuthContext auth);
    Task<bool> DeleteAsync(string id, AuthContext auth);
    Task<string?> GetSummaryAsync(string id);
}
