using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CleanupService : BackgroundService
{
    private readonly IServiceProvider _provider;
    private readonly int _intervalMinutes;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IServiceProvider provider, IOptions<CarbonFilesOptions> options, ILogger<CleanupService> logger)
    {
        _provider = provider;
        _intervalMinutes = options.Value.CleanupIntervalMinutes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first cleanup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredBucketsAsync(stoppingToken);
                await CleanupOrphanedContentAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    internal async Task CleanupExpiredBucketsAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CleanupRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorageService>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var now = DateTime.UtcNow;
        var expired = await repo.GetExpiredBucketsAsync(now, ct);

        if (expired.Count == 0) return;

        _logger.LogInformation("Cleaning up {Count} expired buckets", expired.Count);

        foreach (var bucket in expired)
        {
            // Delete files from disk
            storage.DeleteBucketDir(bucket.Id);

            // Invalidate cache entries for this bucket
            cache.InvalidateBucket(bucket.Id);
            cache.InvalidateFilesForBucket(bucket.Id);
            cache.InvalidateShortUrlsForBucket(bucket.Id);
            cache.InvalidateUploadTokensForBucket(bucket.Id);

            // Delete bucket and all associated DB records in a single transaction
            await repo.DeleteBucketAndRelatedAsync(bucket.Id, ct);
        }
        cache.InvalidateStats();
        _logger.LogInformation("Cleaned up {Count} expired buckets", expired.Count);
    }

    internal async Task CleanupOrphanedContentAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CleanupRepository>();
        var contentStorage = scope.ServiceProvider.GetRequiredService<ContentStorageService>();

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var orphans = await repo.GetOrphanedContentAsync(cutoff, ct);

        if (orphans.Count == 0) return;

        _logger.LogInformation("Cleaning up {Count} orphaned content objects", orphans.Count);

        foreach (var orphan in orphans)
        {
            contentStorage.Delete(orphan.DiskPath);
            await repo.DeleteContentObjectAsync(orphan.Hash, ct);
        }

        _logger.LogInformation("Cleaned up {Count} orphaned content objects", orphans.Count);
    }
}
