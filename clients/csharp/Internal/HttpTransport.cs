using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Internal;

internal class HttpTransport
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    internal readonly JsonSerializerOptions JsonOptions;

    public HttpTransport(HttpClient http, string? apiKey, JsonSerializerOptions? jsonOptions = null)
    {
        _http = http;
        _apiKey = apiKey;
        JsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string? ApiKey => _apiKey;
    public Uri? BaseAddress => _http.BaseAddress;

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (_apiKey != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        return await SendAsync<T>(request, ct);
    }

    public async Task<T> GetAsync<T>(string path, Dictionary<string, string?>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        using var request = CreateRequest(HttpMethod.Get, url);
        return await SendAsync<T>(request, ct);
    }

    public async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request, ct);
        await ThrowIfErrorAsync(response, ct);
        return await response.Content.ReadAsStringAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );
    }

    /// <summary>
    /// Returns a stream over the response body. The underlying <see cref="HttpResponseMessage"/>
    /// is intentionally not disposed here; disposing the returned stream releases the connection.
    /// </summary>
    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await ThrowIfErrorAsync(response, ct);
        return await response.Content.ReadAsStreamAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await SendAsync<TResponse>(request, ct);
    }

    public async Task PostAsync<TRequest>(string path, TRequest body, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        var response = await _http.SendAsync(request, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    public async Task<TResponse> PatchAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        using var request = CreateRequest(new HttpMethod("PATCH"), path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await SendAsync<TResponse>(request, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Delete, path);
        var response = await _http.SendAsync(request, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    public async Task<TResponse> SendMultipartAsync<TResponse>(string path, HttpContent content, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = content;
        return await SendAsync<TResponse>(request, ct);
    }

    public async Task<TResponse> PutStreamAsync<TResponse>(string path, HttpContent content, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Put, path);
        request.Content = content;
        return await SendAsync<TResponse>(request, ct);
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_apiKey != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return await _http.SendAsync(request, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct);
        await ThrowIfErrorAsync(response, ct);
        var json = await response.Content.ReadAsStringAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new CarbonFilesException(response.StatusCode, "Failed to deserialize response");
    }

    internal async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(
#if !NETSTANDARD2_0
            ct
#endif
        );

        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
            if (error?.Error != null)
                throw new CarbonFilesException(response.StatusCode, error.Error, error.Hint);
        }
        catch (JsonException) { }

        throw new CarbonFilesException(response.StatusCode, body);
    }

    internal static string BuildUrl(string path, Dictionary<string, string?>? query)
    {
        if (query == null || query.Count == 0) return path;

        var sb = new StringBuilder(path);
        var first = true;
        foreach (var kvp in query)
        {
            if (kvp.Value == null) continue;
            sb.Append(first ? '?' : '&');
            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }
        return sb.ToString();
    }
}
