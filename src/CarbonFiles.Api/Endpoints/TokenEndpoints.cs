using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Endpoints;

public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens/dashboard").WithTags("Dashboard Tokens");

        // POST /api/tokens/dashboard — Create dashboard token (Admin only)
        group.MapPost("/", async (CreateDashboardTokenRequest? request, HttpContext ctx, IDashboardTokenService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.TokenEndpoints");
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            try
            {
                var result = await svc.CreateAsync(request?.ExpiresIn);
                logger.LogInformation("Dashboard token created, expires {ExpiresAt}", result.ExpiresAt.ToString("o"));
                return Results.Created("/api/tokens/dashboard/me", result);
            }
            catch (ArgumentException ex)
            {
                return ApiResults.BadRequest(ex.Message);
            }
        })
        .Produces<DashboardTokenResponse>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .WithSummary("Create dashboard token")
        .WithDescription("Auth: Admin only. Creates a short-lived JWT token for dashboard access with optional custom expiry.");

        // GET /api/tokens/dashboard/me — Validate current token
        // This endpoint directly parses the Bearer token since it needs the raw JWT string
        // for DashboardTokenService.ValidateTokenAsync, not just the resolved AuthContext.
        group.MapGet("/me", async (HttpContext ctx, IDashboardTokenService svc) =>
        {
            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? authHeader["Bearer ".Length..]
                : null;

            if (token == null)
                return ApiResults.Error("No token provided", 401);

            var info = await svc.ValidateTokenAsync(token);
            return info != null ? Results.Ok(info) : ApiResults.Error("Invalid or expired token", 401);
        })
        .Produces<DashboardTokenInfo>(200)
        .Produces<ErrorResponse>(401)
        .WithSummary("Validate dashboard token")
        .WithDescription("Auth: Dashboard token (Bearer). Validates the current dashboard token and returns its metadata (expiry, issued at).");
    }
}
