using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Endpoints;

public static class UploadTokenEndpoints
{
    public static void MapUploadTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/buckets/{id}/tokens — Create upload token (owner or admin)
        app.MapPost("/api/buckets/{id}/tokens", async (string id, CreateUploadTokenRequest? request, HttpContext ctx, IUploadTokenService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.UploadTokenEndpoints");
            if (ctx.RequireAuth(out var auth) is { } err) return err;

            try
            {
                var result = await svc.CreateAsync(id, request ?? new(), auth);
                if (result == null)
                    return ApiResults.NotFound("Bucket not found or access denied");

                logger.LogInformation("Upload token created for bucket {BucketId}", id);
                return Results.Created($"/api/buckets/{id}/tokens", result);
            }
            catch (ArgumentException ex)
            {
                return ApiResults.BadRequest(ex.Message);
            }
        })
        .Produces<UploadTokenResponse>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithTags("Upload Tokens")
        .WithSummary("Create upload token")
        .WithDescription("Auth: Bucket owner or admin. Creates a scoped upload token for a specific bucket with optional expiry and upload limit.");
    }
}
