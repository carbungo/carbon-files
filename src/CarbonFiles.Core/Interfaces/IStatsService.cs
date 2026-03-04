using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IStatsService
{
    Task<StatsResponse> GetStatsAsync(CancellationToken ct = default);
}
