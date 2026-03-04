using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Endpoints;

public static class KeyEndpoints
{
    public static void MapKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keys").WithTags("API Keys");

        // POST /api/keys — Create API key (Admin only)
        group.MapPost("/", async (CreateApiKeyRequest request, HttpContext ctx, IApiKeyService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.KeyEndpoints");
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            if (string.IsNullOrWhiteSpace(request.Name))
                return ApiResults.BadRequest("Name is required");

            var result = await svc.CreateAsync(request.Name);
            logger.LogInformation("API key created: {Prefix} ({Name})", result.Prefix, request.Name);
            return Results.Created($"/api/keys/{result.Prefix}", result);
        })
        .Produces<ApiKeyResponse>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .WithSummary("Create API key")
        .WithDescription("Auth: Admin only. Creates a new API key scoped to its own buckets. Returns the full key (only shown once).");

        // GET /api/keys — List all API keys (Admin only, paginated)
        group.MapGet("/", async (HttpContext ctx, IApiKeyService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            var result = await svc.ListAsync(new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        })
        .Produces<PaginatedResponse<ApiKeyListItem>>(200)
        .Produces<ErrorResponse>(403)
        .WithSummary("List API keys")
        .WithDescription("Auth: Admin only. Returns a paginated list of all API keys (secrets are masked).");

        // DELETE /api/keys/{prefix} — Revoke API key (Admin only)
        group.MapDelete("/{prefix}", async (string prefix, HttpContext ctx, IApiKeyService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.KeyEndpoints");
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            var deleted = await svc.DeleteAsync(prefix);
            if (deleted)
            {
                logger.LogInformation("API key deleted: {Prefix}", prefix);
                return Results.NoContent();
            }
            return ApiResults.NotFound("API key not found");
        })
        .Produces(204)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithSummary("Revoke API key")
        .WithDescription("Auth: Admin only. Permanently revokes an API key by its prefix.");

        // GET /api/keys/{prefix}/usage — Detailed key usage (Admin only)
        group.MapGet("/{prefix}/usage", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            if (ctx.RequireAdmin(out var auth) is { } err) return err;

            var result = await svc.GetUsageAsync(prefix);
            return result != null ? Results.Ok(result) : ApiResults.NotFound("API key not found");
        })
        .Produces<ApiKeyUsageResponse>(200)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithSummary("Get API key usage")
        .WithDescription("Auth: Admin only. Returns detailed usage statistics for an API key (bucket count, file count, total size).");
    }
}
