using System.IO.Compression;
using CarbonFiles.Api.Auth;
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
            if (ctx.RequireAuth(out var auth) is { } err) return err;

            if (string.IsNullOrWhiteSpace(request.Name))
                return ApiResults.BadRequest("Name is required");

            try
            {
                var result = await svc.CreateAsync(request, auth);
                logger.LogInformation("Bucket created: {BucketId} by {Owner}", result.Id, auth.IsAdmin ? "admin" : auth.OwnerName);
                return Results.Created($"/api/buckets/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning("Bucket creation failed: {Error}", ex.Message);
                return ApiResults.BadRequest(ex.Message);
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
            if (ctx.RequireAuth(out var auth) is { } err) return err;

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

        // GET /api/buckets/{id} — Get bucket detail (public access)
        group.MapGet("/{id}", async (string id, IBucketService svc, string? include) =>
        {
            var includeFiles = string.Equals(include, "files", StringComparison.OrdinalIgnoreCase);
            var result = await svc.GetByIdAsync(id, includeFiles);
            return result != null
                ? Results.Ok(result)
                : ApiResults.NotFound("Bucket not found");
        })
        .Produces<BucketDetailResponse>(200)
        .Produces<ErrorResponse>(404)
        .WithSummary("Get bucket")
        .WithDescription("Public. Returns bucket details. Use ?include=files to include file list.");

        // PATCH /api/buckets/{id} — Update bucket (owner or admin)
        group.MapPatch("/{id}", async (string id, UpdateBucketRequest request, HttpContext ctx, IBucketService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            if (ctx.RequireAuth(out var auth) is { } err) return err;

            // At least one field required
            if (request.Name == null && request.Description == null && request.ExpiresIn == null)
                return ApiResults.BadRequest("At least one field is required");

            try
            {
                var result = await svc.UpdateAsync(id, request, auth);
                if (result == null)
                {
                    // Need to distinguish 404 vs 403 — check if bucket exists
                    var existing = await svc.GetByIdAsync(id);
                    if (existing == null)
                        return ApiResults.NotFound("Bucket not found");
                    return ApiResults.Forbidden("Access denied");
                }
                logger.LogInformation("Bucket {BucketId} updated", id);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return ApiResults.BadRequest(ex.Message);
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
            if (ctx.RequireAuth(out var auth) is { } err) return err;

            var result = await svc.DeleteAsync(id, auth);
            if (!result)
            {
                // Need to distinguish 404 vs 403 — check if bucket exists
                var existing = await svc.GetByIdAsync(id);
                if (existing == null)
                    return ApiResults.NotFound("Bucket not found");
                return ApiResults.Forbidden("Access denied");
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
                : ApiResults.NotFound("Bucket not found");
        })
        .Produces<string>(200, "text/plain")
        .Produces<ErrorResponse>(404)
        .WithSummary("Get bucket summary")
        .WithDescription("Public. Returns a plaintext summary of the bucket suitable for LLM context or previews.");

        // GET|HEAD /api/buckets/{id}/zip — Download bucket as ZIP (public access)
        group.MapMethods("/{id}/zip", new[] { "GET", "HEAD" }, async (string id, HttpContext ctx, IBucketService svc, FileStorageService storage, ContentStorageService contentStorage, IFileService fileService, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
            var bucket = await svc.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            // Log warning for large buckets
            if (bucket.FileCount > 10000 || bucket.TotalSize > 10L * 1024 * 1024 * 1024)
                logger.LogWarning("Large bucket ZIP requested: {BucketId} ({FileCount} files, {TotalSize} bytes)", id, bucket.FileCount, bucket.TotalSize);

            var files = await svc.GetAllFilesAsync(id);

            ctx.Response.ContentType = "application/zip";
            var safeName = bucket.Name.Replace("\"", "'").Replace("\r", "").Replace("\n", "");
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{safeName}.zip\"";

            // HEAD request: return headers without body
            if (HttpMethods.IsHead(ctx.Request.Method))
                return Results.Empty;

            // ZipArchive.Dispose writes the central directory synchronously,
            // so we must allow synchronous IO on this request.
            var syncIoFeature = ctx.Features.Get<IHttpBodyControlFeature>();
            if (syncIoFeature != null)
                syncIoFeature.AllowSynchronousIO = true;

            using var archive = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true);

            // Create explicit directory entries so all extractors produce nested folders
            var directories = new HashSet<string>();
            foreach (var file in files)
            {
                var dir = file.Path.Replace('\\', '/');
                var idx = dir.LastIndexOf('/');
                while (idx > 0)
                {
                    dir = dir[..idx];
                    if (!directories.Add(dir + "/"))
                        break; // parent dirs already added
                    idx = dir.LastIndexOf('/');
                }
            }

            foreach (var dir in directories.OrderBy(d => d))
                archive.CreateEntry(dir);

            foreach (var file in files)
            {
                var entryPath = file.Path.Replace('\\', '/');
                var entry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();

                FileStream? fileStream = null;
                if (file.Sha256 != null)
                {
                    var diskPath = await fileService.GetContentDiskPathAsync(id, file.Path);
                    if (diskPath != null)
                        fileStream = contentStorage.OpenRead(diskPath);
                }
                else
                {
                    fileStream = storage.OpenRead(id, file.Path);
                }

                if (fileStream != null)
                {
                    await using (fileStream)
                        await fileStream.CopyToAsync(entryStream);
                }
            }

            return Results.Empty;
        })
        .Produces(200, contentType: "application/zip")
        .Produces<ErrorResponse>(404)
        .WithSummary("Download bucket as ZIP")
        .WithDescription("Public. Streams all files in the bucket as a ZIP archive. Supports HEAD requests for headers only.");
    }
}
