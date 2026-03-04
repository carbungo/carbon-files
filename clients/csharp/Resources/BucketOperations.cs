using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class BucketOperations
{
    private readonly HttpTransport _transport;
    internal BucketOperations(HttpTransport transport) => _transport = transport;

    public BucketResource this[string id] => new(_transport, id);

    public Task<Bucket> CreateAsync(CreateBucketRequest request, CancellationToken ct = default)
        => _transport.PostAsync<CreateBucketRequest, Bucket>("/api/buckets", request, ct);

    public Task<PaginatedResponse<Bucket>> ListAsync(
        PaginationOptions? pagination = null,
        bool? includeExpired = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (pagination?.Limit != null) query["limit"] = pagination.Limit.Value.ToString();
        if (pagination?.Offset != null) query["offset"] = pagination.Offset.Value.ToString();
        if (pagination?.Sort != null) query["sort"] = pagination.Sort;
        if (pagination?.Order != null) query["order"] = pagination.Order;
        if (includeExpired != null) query["include_expired"] = includeExpired.Value.ToString().ToLowerInvariant();
        return _transport.GetAsync<PaginatedResponse<Bucket>>("/api/buckets", query, ct);
    }
}
