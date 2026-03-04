using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IDashboardTokenService
{
    Task<DashboardTokenResponse> CreateAsync(string? expiresIn);
    Task<DashboardTokenInfo?> ValidateTokenAsync(string token);
}
