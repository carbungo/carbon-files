using System.Text.Json;
using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class FileResource
{
    private readonly HttpTransport _transport;
    private readonly string _bucketId;
    private readonly string _path;

    internal FileResource(HttpTransport transport, string bucketId, string path)
    {
        _transport = transport;
        _bucketId = bucketId;
        _path = path;
    }

    private string BasePath => $"/api/buckets/{Uri.EscapeDataString(_bucketId)}/files/{Uri.EscapeDataString(_path)}";

    public Task<BucketFile> GetMetadataAsync(CancellationToken ct = default)
        => _transport.GetAsync<BucketFile>(BasePath, ct);

    public Task<Stream> DownloadAsync(CancellationToken ct = default)
        => _transport.GetStreamAsync($"{BasePath}/content", ct);

    public Task DeleteAsync(CancellationToken ct = default)
        => _transport.DeleteAsync(BasePath, ct);

    public async Task<BucketFile> PatchAsync(Stream content, long rangeStart, long rangeEnd, long totalSize, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{BasePath}/content");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.Add("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{totalSize}");

        using var response = await _transport.SendRawAsync(request, ct);
        await _transport.ThrowIfErrorAsync(response, ct);
        var json = await response.Content.ReadAsStringAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );
        return JsonSerializer.Deserialize<BucketFile>(json, _transport.JsonOptions)
            ?? throw new CarbonFilesException(response.StatusCode, "Failed to deserialize response");
    }

    public async Task<BucketFile> AppendAsync(Stream content, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{BasePath}/content");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add("X-Append", "true");

        using var response = await _transport.SendRawAsync(request, ct);
        await _transport.ThrowIfErrorAsync(response, ct);
        var json = await response.Content.ReadAsStringAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );
        return JsonSerializer.Deserialize<BucketFile>(json, _transport.JsonOptions)
            ?? throw new CarbonFilesException(response.StatusCode, "Failed to deserialize response");
    }
}
