using System.IO.Compression;
using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Services;
using Microsoft.AspNetCore.Http.Features;

namespace CarbonFiles.Api.Endpoints;

public static class BucketEndpoints
{
    public static void MapBucketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/buckets").WithTags("Buckets");

        // POST /api/buckets — Create bucket (API key or admin, NOT public)
        group.MapPost("/", async (CreateBucketRequest request, HttpContext ctx, IBucketService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Json(new ErrorResponse { Error = "Name is required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            try
            {
                var result = await svc.CreateAsync(request, auth);
                logger.LogInformation("Bucket created: {BucketId} by {Owner}", result.Id, auth.IsAdmin ? "admin" : auth.OwnerName);
                return Results.Created($"/api/buckets/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning("Bucket creation failed: {Error}", ex.Message);
                return Results.Json(new ErrorResponse { Error = ex.Message }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);
            }
        })
        .Produces<Bucket>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .WithSummary("Create bucket")
        .WithDescription("Auth: API key owner or admin. Creates a new file bucket with optional expiry and description.");

        // GET /api/buckets — List buckets (admin sees all, API key sees own)
        group.MapGet("/", async (HttpContext ctx, IBucketService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc",
            bool include_expired = false) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            // Only admin can include expired
            var includeExpired = include_expired && auth.IsAdmin;

            var result = await svc.ListAsync(
                new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order },
                auth,
                includeExpired);
            return Results.Ok(result);
        })
        .Produces<PaginatedResponse<Bucket>>(200)
        .Produces<ErrorResponse>(403)
        .WithSummary("List buckets")
        .WithDescription("Auth: API key owner or admin. Returns a paginated list of buckets. Admin sees all; API key sees own buckets only.");

        // GET /api/buckets/{id} — Get bucket with files (public access)
        group.MapGet("/{id}", async (string id, IBucketService svc) =>
        {
            var result = await svc.GetByIdAsync(id);
            return result != null
                ? Results.Ok(result)
                : Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .Produces<BucketDetailResponse>(200)
        .Produces<ErrorResponse>(404)
        .WithSummary("Get bucket")
        .WithDescription("Public. Returns bucket details including file list.");

        // PATCH /api/buckets/{id} — Update bucket (owner or admin)
        group.MapPatch("/{id}", async (string id, UpdateBucketRequest request, HttpContext ctx, IBucketService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            // At least one field required
            if (request.Name == null && request.Description == null && request.ExpiresIn == null)
                return Results.Json(new ErrorResponse { Error = "At least one field is required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            try
            {
                var result = await svc.UpdateAsync(id, request, auth);
                if (result == null)
                {
                    // Need to distinguish 404 vs 403 — check if bucket exists
                    var existing = await svc.GetByIdAsync(id);
                    if (existing == null)
                        return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
                    return Results.Json(new ErrorResponse { Error = "Access denied" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);
                }
                logger.LogInformation("Bucket {BucketId} updated", id);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorResponse { Error = ex.Message }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);
            }
        })
        .Produces<Bucket>(200)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithSummary("Update bucket")
        .WithDescription("Auth: Bucket owner or admin. Updates bucket name, description, or expiry.");

        // DELETE /api/buckets/{id} — Delete bucket (owner or admin)
        group.MapDelete("/{id}", async (string id, HttpContext ctx, IBucketService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var result = await svc.DeleteAsync(id, auth);
            if (!result)
            {
                // Need to distinguish 404 vs 403 — check if bucket exists
                var existing = await svc.GetByIdAsync(id);
                if (existing == null)
                    return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
                return Results.Json(new ErrorResponse { Error = "Access denied" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);
            }
            logger.LogInformation("Bucket {BucketId} deleted", id);
            return Results.NoContent();
        })
        .Produces(204)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithSummary("Delete bucket")
        .WithDescription("Auth: Bucket owner or admin. Permanently deletes a bucket and all its files.");

        // GET /api/buckets/{id}/summary — Plaintext summary (public access)
        group.MapGet("/{id}/summary", async (string id, IBucketService svc) =>
        {
            var result = await svc.GetSummaryAsync(id);
            return result != null
                ? Results.Text(result, "text/plain")
                : Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .Produces<string>(200, "text/plain")
        .Produces<ErrorResponse>(404)
        .WithSummary("Get bucket summary")
        .WithDescription("Public. Returns a plaintext summary of the bucket suitable for LLM context or previews.");

        // GET|HEAD /api/buckets/{id}/zip — Download bucket as ZIP (public access)
        group.MapMethods("/{id}/zip", new[] { "GET", "HEAD" }, async (string id, HttpContext ctx, IBucketService svc, FileStorageService storage, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            var bucket = await svc.GetBucketAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Log warning for large buckets
            if (bucket.FileCount > 10000 || bucket.TotalSize > 10L * 1024 * 1024 * 1024)
                logger.LogWarning("Large bucket ZIP requested: {BucketId} ({FileCount} files, {TotalSize} bytes)", id, bucket.FileCount, bucket.TotalSize);

            var files = await svc.GetAllFilesAsync(id);

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{bucket.Name}.zip\"";

            // HEAD request: return headers without body
            if (HttpMethods.IsHead(ctx.Request.Method))
                return Results.Empty;

            // ZipArchive.Dispose writes the central directory synchronously,
            // so we must allow synchronous IO on this request.
            var syncIoFeature = ctx.Features.Get<IHttpBodyControlFeature>();
            if (syncIoFeature != null)
                syncIoFeature.AllowSynchronousIO = true;

            using var archive = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = storage.OpenRead(id, file.Path);
                if (fileStream != null)
                    await fileStream.CopyToAsync(entryStream);
            }

            return Results.Empty;
        })
        .Produces(200, contentType: "application/zip")
        .Produces<ErrorResponse>(404)
        .WithSummary("Download bucket as ZIP")
        .WithDescription("Public. Streams all files in the bucket as a ZIP archive. Supports HEAD requests for headers only.");
    }
}
