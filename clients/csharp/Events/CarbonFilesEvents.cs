using CarbonFiles.Client.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace CarbonFiles.Client.Events;

public class CarbonFilesEvents :
#if !NETSTANDARD2_0
    IAsyncDisposable
#else
    IDisposable
#endif
{
    private readonly HubConnection _connection;

    public CarbonFilesEvents(Uri baseAddress, string? apiKey = null)
    {
        var hubUrl = new Uri(baseAddress, "/hub/files");
        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl.ToString(), options =>
            {
                if (apiKey != null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(apiKey);
            })
            .WithAutomaticReconnect();

        _connection = builder.Build();
    }

    internal CarbonFilesEvents(HubConnection connection)
    {
        _connection = connection;
    }

    public Task ConnectAsync(CancellationToken ct = default)
        => _connection.StartAsync(ct);

    public Task DisconnectAsync(CancellationToken ct = default)
        => _connection.StopAsync(ct);

    // Subscription methods
    public Task SubscribeToBucketAsync(string bucketId, CancellationToken ct = default)
        => _connection.InvokeAsync("SubscribeToBucket", bucketId, ct);

    public Task UnsubscribeFromBucketAsync(string bucketId, CancellationToken ct = default)
        => _connection.InvokeAsync("UnsubscribeFromBucket", bucketId, ct);

    public Task SubscribeToFileAsync(string bucketId, string path, CancellationToken ct = default)
        => _connection.InvokeAsync("SubscribeToFile", bucketId, path, ct);

    public Task UnsubscribeFromFileAsync(string bucketId, string path, CancellationToken ct = default)
        => _connection.InvokeAsync("UnsubscribeFromFile", bucketId, path, ct);

    public Task SubscribeToAllAsync(CancellationToken ct = default)
        => _connection.InvokeAsync("SubscribeToAll", ct);

    public Task UnsubscribeFromAllAsync(CancellationToken ct = default)
        => _connection.InvokeAsync("UnsubscribeFromAll", ct);

    // Typed event handlers - each returns IDisposable for unsubscription
    public IDisposable OnFileCreated(Func<string, BucketFile, Task> handler)
        => _connection.On<string, BucketFile>("FileCreated", handler);

    public IDisposable OnFileUpdated(Func<string, BucketFile, Task> handler)
        => _connection.On<string, BucketFile>("FileUpdated", handler);

    public IDisposable OnFileDeleted(Func<string, string, Task> handler)
        => _connection.On<string, string>("FileDeleted", handler);

    public IDisposable OnBucketCreated(Func<Bucket, Task> handler)
        => _connection.On<Bucket>("BucketCreated", handler);

    public IDisposable OnBucketUpdated(Func<string, BucketChanges, Task> handler)
        => _connection.On<string, BucketChanges>("BucketUpdated", handler);

    public IDisposable OnBucketDeleted(Func<string, Task> handler)
        => _connection.On<string>("BucketDeleted", handler);

#if !NETSTANDARD2_0
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
#else
    public void Dispose()
    {
        _connection.DisposeAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
#endif
}
