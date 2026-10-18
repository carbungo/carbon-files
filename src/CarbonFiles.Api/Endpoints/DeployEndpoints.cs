using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;

namespace CarbonFiles.Api.Endpoints;

public static class DeployEndpoints
{
    public static void MapDeployEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/buckets/{id}/deploy/sync", async (string id, SyncDeployRequest request,
            HttpContext ctx, IBucketService bucketService, IFileService fileService, IUploadTokenService tokens) =>
        {
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null) return ApiResults.NotFound("Bucket not found");

            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token)) return ApiResults.Forbidden("Authentication required");
                var (tokenBucket, valid) = await tokens.ValidateAsync(token);
                if (!valid || tokenBucket != id) return ApiResults.Forbidden("Invalid or expired upload token");
            }
            else if (!auth.CanManage(bucket.Owner))
                return ApiResults.Forbidden("Access denied");

            if (request.Files == null || request.Files.Count is 0 or > 10000)
                return ApiResults.BadRequest("Manifest must contain 1 to 10000 files");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in request.Files)
            {
                if (file == null || string.IsNullOrEmpty(file.Path) || string.IsNullOrEmpty(file.Sha256) ||
                    file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                    return ApiResults.BadRequest("Invalid manifest entry");
                try
                {
                    if (PathNormalizer.Normalize(file.Path) != file.Path || !seen.Add(file.Path))
                        return ApiResults.BadRequest("Manifest paths must be unique and normalized");
                }
                catch (ArgumentException)
                {
                    return ApiResults.BadRequest("Invalid manifest path");
                }
            }

            var result = await fileService.SyncDeployAsync(id, request.Files, ctx.RequestAborted);
            return result == null
                ? ApiResults.Error("Manifest does not match uploaded files", 409, "Upload every listed file with its expected SHA-256 before syncing.")
                : Results.Ok(result);
        })
        .Produces<SyncDeployResponse>(200)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(409)
        .WithTags("Deploys")
        .WithSummary("Prune files absent from a verified deploy manifest")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=) scoped to this bucket. Verifies every path and SHA-256, then removes all other bucket files in one transaction. An empty manifest is rejected. This replaces the entire bucket file set.");
    }
}
