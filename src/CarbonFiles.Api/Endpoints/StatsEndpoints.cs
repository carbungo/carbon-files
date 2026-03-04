using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (HttpContext ctx, IStatsService statsService, ICacheService cache, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.StatsEndpoints");
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            var cachedStats = cache.GetStats();
            if (cachedStats != null)
                return Results.Ok(cachedStats);

            var stats = await statsService.GetStatsAsync();

            logger.LogDebug("Stats queried: {BucketCount} buckets, {FileCount} files", stats.TotalBuckets, stats.TotalFiles);
            cache.SetStats(stats);
            return Results.Ok(stats);
        })
        .Produces<StatsResponse>(200)
        .Produces<ErrorResponse>(403)
        .WithTags("Stats")
        .WithSummary("Get system statistics")
        .WithDescription("Auth: Admin only. Returns system-wide statistics including total buckets, files, storage, and per-owner breakdowns.");
    }
}
