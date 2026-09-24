using System.Collections.Concurrent;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class BucketActivityService : BackgroundService, IBucketActivityService
{
    private readonly string _connectionString;
    private readonly TimeSpan _interval;
    private readonly ILogger<BucketActivityService> _logger;
    private ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public BucketActivityService(
        IOptions<CarbonFilesOptions> options,
        ILogger<BucketActivityService> logger)
    {
        _connectionString = $"Data Source={options.Value.DbPath}";
        _interval = TimeSpan.FromSeconds(options.Value.BucketActivityIntervalSeconds);
        _logger = logger;
    }

    public void Touch(string bucketId)
    {
        while (true)
        {
            var pending = Volatile.Read(ref _pending);
            pending.TryAdd(bucketId, 0);

            // If a drain swapped dictionaries while this add was in flight, also
            // add to the new dictionary so the touch cannot miss both snapshots.
            if (ReferenceEquals(pending, Volatile.Read(ref _pending)))
                return;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await DrainSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The final drain is performed by StopAsync after this loop exits.
        }
    }

    internal async Task<IReadOnlyCollection<string>> DrainAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Interlocked.Exchange(
            ref _pending,
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        if (snapshot.IsEmpty)
            return Array.Empty<string>();

        var bucketIds = snapshot.Keys.ToArray();

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            var parameterNames = new string[bucketIds.Length];
            for (var i = 0; i < bucketIds.Length; i++)
            {
                parameterNames[i] = $"@id{i}";
                command.Parameters.AddWithValue(parameterNames[i], bucketIds[i]);
            }

            command.CommandText = $"UPDATE Buckets SET LastUsedAt = @now WHERE Id IN ({string.Join(", ", parameterNames)})";
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Updated LastUsedAt for {Count} buckets", bucketIds.Length);
            return bucketIds;
        }
        catch
        {
            foreach (var bucketId in bucketIds)
                Touch(bucketId);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DrainSafelyAsync(CancellationToken.None);
    }

    private async Task DrainSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DrainAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown will retry without cancellation in StopAsync.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update bucket LastUsedAt values");
        }
    }
}
