using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Services;
using Microsoft.Net.Http.Headers;

namespace CarbonFiles.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/buckets/{id}/files — List files (public, paginated)
        app.MapGet("/api/buckets/{id}/files", async (string id, IFileService fileService, IBucketService bucketService,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            var result = await fileService.ListAsync(id,
                new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        })
        .Produces<PaginatedResponse<BucketFile>>(200)
        .Produces<ErrorResponse>(404)
        .WithTags("Files")
        .WithSummary("List files in bucket")
        .WithDescription("Public. Returns a paginated list of files in the specified bucket.");

        // GET|HEAD /api/buckets/{id}/files/{*filePath} — File metadata or content download
        app.MapMethods("/api/buckets/{id}/files/{*filePath}", new[] { "GET", "HEAD" },
            async (string id, string filePath, HttpContext ctx,
            IFileService fileService, FileStorageService storageService, IBucketService bucketService) =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            if (filePath.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
            {
                var actualPath = filePath[..^"/content".Length];
                return await ServeFileContent(id, actualPath, ctx, fileService, storageService);
            }

            // Return file metadata
            var meta = await fileService.GetMetadataAsync(id, filePath);
            return meta != null
                ? Results.Ok(meta)
                : Results.Json(new ErrorResponse { Error = "File not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .Produces<BucketFile>(200)
        .Produces(206)
        .Produces(304)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(416)
        .WithTags("Files")
        .WithSummary("Get file metadata or download content")
        .WithDescription("Public. Returns file metadata at /files/{path}, or streams file content at /files/{path}/content. Supports Range requests, ETag, and conditional headers.");

        // DELETE /api/buckets/{id}/files/{*filePath} — Delete file (owner or admin)
        app.MapDelete("/api/buckets/{id}/files/{*filePath}", async (string id, string filePath, HttpContext ctx,
            IFileService fileService, IBucketService bucketService, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.FileEndpoints");
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var deleted = await fileService.DeleteAsync(id, filePath, auth);
            if (deleted)
            {
                logger.LogInformation("File {FilePath} deleted from bucket {BucketId}", filePath, id);
                return Results.NoContent();
            }
            return Results.Json(new ErrorResponse { Error = "File not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .Produces(204)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .WithTags("Files")
        .WithSummary("Delete file")
        .WithDescription("Auth: Bucket owner or admin. Permanently deletes a file from the bucket.");

        // PATCH /api/buckets/{id}/files/{*filePath}/content — Partial file update
        app.MapMethods("/api/buckets/{id}/files/{*filePath}", new[] { "PATCH" }, async (string id, string filePath, HttpContext ctx,
            IFileService fileService, FileStorageService storageService, IBucketService bucketService, IUploadTokenService uploadTokenService, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.FileEndpoints");
            // Only handle paths ending with /content
            if (!filePath.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();

            var actualPath = filePath[..^"/content".Length];

            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key, admin key, or upload token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                // Validate upload token
                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return Results.Json(new ErrorResponse { Error = "Invalid or expired upload token" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                auth = AuthContext.Admin();
            }

            // Check if file exists
            var meta = await fileService.GetMetadataAsync(id, actualPath);
            if (meta == null)
                return Results.Json(new ErrorResponse { Error = "File not found", Hint = "Use upload endpoints to create files." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Check X-Append header
            var isAppend = ctx.Request.Headers["X-Append"].FirstOrDefault()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            long offset = 0;
            if (!isAppend)
            {
                // Parse Content-Range header
                var contentRange = ctx.Request.Headers.ContentRange.FirstOrDefault();
                if (contentRange == null || !contentRange.StartsWith("bytes "))
                    return Results.Json(new ErrorResponse { Error = "Content-Range header required for non-append PATCH" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

                var rangePart = contentRange["bytes ".Length..];
                var slashIndex = rangePart.IndexOf('/');
                if (slashIndex > 0)
                    rangePart = rangePart[..slashIndex];

                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex < 0 || !long.TryParse(rangePart[..dashIndex], out offset))
                    return Results.Json(new ErrorResponse { Error = "Invalid Content-Range" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

                // Validate range
                if (offset < 0 || offset > meta.Size)
                    return Results.Json(new ErrorResponse { Error = "Range not satisfiable" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 416);
            }

            var newSize = await storageService.PatchFileAsync(id, actualPath, ctx.Request.Body, offset, isAppend);
            if (newSize < 0)
                return Results.NotFound();

            // Update file metadata in DB
            await fileService.UpdateFileSizeAsync(id, actualPath, newSize);

            logger.LogInformation("File {FilePath} patched in bucket {BucketId}", actualPath, id);
            return Results.Ok(await fileService.GetMetadataAsync(id, actualPath));
        })
        .Produces<BucketFile>(200)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(416)
        .WithTags("Files")
        .WithSummary("Patch file content")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=). Writes to a byte range of an existing file using Content-Range, or appends with X-Append: true.");
    }

    private static async Task<IResult> ServeFileContent(string bucketId, string path, HttpContext ctx,
        IFileService fileService, FileStorageService storageService)
    {
        var meta = await fileService.GetMetadataAsync(bucketId, path);
        if (meta == null)
            return Results.Json(new ErrorResponse { Error = "File not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

        var etag = $"\"{meta.Size}-{meta.UpdatedAt.Ticks}\"";
        var lastModified = meta.UpdatedAt;

        // Conditional request: If-None-Match
        if (ctx.Request.Headers.IfNoneMatch.FirstOrDefault() == etag)
            return Results.StatusCode(304);

        // Conditional request: If-Modified-Since
        if (ctx.Request.Headers.IfModifiedSince.Count > 0)
        {
            if (DateTimeOffset.TryParse(ctx.Request.Headers.IfModifiedSince, out var ifModifiedSince))
            {
                if (lastModified <= ifModifiedSince.UtcDateTime.AddSeconds(1))
                    return Results.StatusCode(304);
            }
        }

        var physicalPath = storageService.GetFilePath(bucketId, path);
        if (!System.IO.File.Exists(physicalPath))
            return Results.Json(new ErrorResponse { Error = "File not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

        // Update last_used_at (fire-and-forget)
        _ = fileService.UpdateLastUsedAsync(bucketId);

        var contentType = meta.MimeType;
        if (contentType.StartsWith("text/") || contentType is "application/json" or "application/xml" or "application/javascript" or "image/svg+xml")
            contentType += "; charset=utf-8";

        ctx.Response.Headers.CacheControl = "public, no-cache";

        string? fileDownloadName = null;
        if (ctx.Request.Query.ContainsKey("download") && ctx.Request.Query["download"] == "true")
            fileDownloadName = meta.Name;

        var etagValue = new EntityTagHeaderValue(etag);
        var lastModifiedOffset = new DateTimeOffset(DateTime.SpecifyKind(lastModified, DateTimeKind.Utc));

        // PhysicalFile uses SendFileAsync internally → Linux sendfile() zero-copy syscall.
        // Handles Range requests (206), If-Range, HEAD, and Accept-Ranges automatically.
        return TypedResults.PhysicalFile(
            physicalPath,
            contentType,
            fileDownloadName: fileDownloadName,
            lastModified: lastModifiedOffset,
            entityTag: etagValue,
            enableRangeProcessing: true);
    }
}
