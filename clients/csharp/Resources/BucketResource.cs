using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class BucketResource
{
    private readonly HttpTransport _transport;
    private readonly string _id;

    internal BucketResource(HttpTransport transport, string id)
    {
        _transport = transport;
        _id = id;
        Files = new FileOperations(transport, id);
        Tokens = new UploadTokenOperations(transport, id);
    }

    public FileOperations Files { get; }
    public UploadTokenOperations Tokens { get; }

    public Task<BucketDetailResponse> GetAsync(CancellationToken ct = default)
        => _transport.GetAsync<BucketDetailResponse>($"/api/buckets/{Uri.EscapeDataString(_id)}", ct);

    public Task<Bucket> UpdateAsync(UpdateBucketRequest request, CancellationToken ct = default)
        => _transport.PatchAsync<UpdateBucketRequest, Bucket>($"/api/buckets/{Uri.EscapeDataString(_id)}", request, ct);

    public Task DeleteAsync(CancellationToken ct = default)
        => _transport.DeleteAsync($"/api/buckets/{Uri.EscapeDataString(_id)}", ct);

    public Task<string> GetSummaryAsync(CancellationToken ct = default)
        => _transport.GetStringAsync($"/api/buckets/{Uri.EscapeDataString(_id)}/summary", ct);

    public Task<Stream> DownloadZipAsync(CancellationToken ct = default)
        => _transport.GetStreamAsync($"/api/buckets/{Uri.EscapeDataString(_id)}/zip", ct);
}
