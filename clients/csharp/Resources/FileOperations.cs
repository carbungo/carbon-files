using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class FileOperations
{
    private readonly HttpTransport _transport;
    private readonly string _bucketId;

    internal FileOperations(HttpTransport transport, string bucketId)
    {
        _transport = transport;
        _bucketId = bucketId;
    }

    public FileResource this[string path] => new(_transport, _bucketId, path);

    public Task<PaginatedResponse<BucketFile>> ListAsync(
        PaginationOptions? pagination = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (pagination != null)
        {
            if (pagination.Limit.HasValue) query["limit"] = pagination.Limit.Value.ToString();
            if (pagination.Offset.HasValue) query["offset"] = pagination.Offset.Value.ToString();
            if (pagination.Sort != null) query["sort"] = pagination.Sort;
            if (pagination.Order != null) query["order"] = pagination.Order;
        }

        var url = HttpTransport.BuildUrl($"/api/buckets/{Uri.EscapeDataString(_bucketId)}/files", query);
        return _transport.GetAsync<PaginatedResponse<BucketFile>>(url, ct);
    }

    public Task<DirectoryListingResponse> ListDirectoryAsync(
        string? path = null,
        PaginationOptions? pagination = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (path != null) query["path"] = path;
        if (pagination != null)
        {
            if (pagination.Limit.HasValue) query["limit"] = pagination.Limit.Value.ToString();
            if (pagination.Offset.HasValue) query["offset"] = pagination.Offset.Value.ToString();
            if (pagination.Sort != null) query["sort"] = pagination.Sort;
            if (pagination.Order != null) query["order"] = pagination.Order;
        }

        var url = HttpTransport.BuildUrl($"/api/buckets/{Uri.EscapeDataString(_bucketId)}/ls", query);
        return _transport.GetAsync<DirectoryListingResponse>(url, ct);
    }

    public Task<UploadResponse> UploadAsync(
        Stream content,
        string filename,
        IProgress<UploadProgress>? progress = null,
        string? uploadToken = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["filename"] = filename
        };
        if (uploadToken != null) query["token"] = uploadToken;

        var url = HttpTransport.BuildUrl($"/api/buckets/{Uri.EscapeDataString(_bucketId)}/upload/stream", query);
        var streamContent = new ProgressStreamContent(content, progress);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return _transport.PutStreamAsync<UploadResponse>(url, streamContent, ct);
    }
}
