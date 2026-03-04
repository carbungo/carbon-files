using CarbonFiles.Api.Auth;
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
        // GET /api/buckets/{id}/files — List files (public, paginated or tree mode)
        app.MapGet("/api/buckets/{id}/files", async (string id, IFileService fileService, IBucketService bucketService,
            string? delimiter, string? prefix, string? cursor,
            int? limit, int? offset, string? sort, string? order) =>
        {
            var bucket = await bucketService.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            if (delimiter != null)
            {
                // Tree mode
                var treeLimit = Math.Clamp(limit ?? 100, 1, 1000);
                var result = await fileService.ListTreeAsync(id, prefix, delimiter, treeLimit, cursor);
                return Results.Ok(result);
            }
            else
            {
                // Flat mode (existing behavior)
                var pagination = new PaginationParams
                {
                    Limit = Math.Clamp(limit ?? 50, 1, 1000),
                    Offset = Math.Max(offset ?? 0, 0),
                    Sort = sort ?? "created_at",
                    Order = order ?? "desc"
                };
                var result = await fileService.ListAsync(id, pagination);
                return Results.Ok(result);
            }
        })
        .Produces<PaginatedResponse<BucketFile>>(200)
        .Produces<FileTreeResponse>(200)
        .Produces<ErrorResponse>(404)
        .WithTags("Files")
        .WithSummary("List files in bucket")
        .WithDescription("Public. Returns a paginated list of files, or tree structure with ?delimiter=/&prefix=.");

        // GET /api/buckets/{id}/ls — List directory contents (public)
        app.MapGet("/api/buckets/{id}/ls", async (string id, IFileService fileService, IBucketService bucketService,
            string path = "", int limit = 200, int offset = 0, string sort = "name", string order = "asc") =>
        {
            var bucket = await bucketService.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            var result = await fileService.ListDirectoryAsync(id, path,
                new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        })
        .Produces<DirectoryListingResponse>(200)
        .Produces<ErrorResponse>(404)
        .WithTags("Files")
        .WithSummary("List directory contents")
        .WithDescription("Public. Returns files and folder names at a specific path level within the bucket.");

        // GET|HEAD /api/buckets/{id}/files/{*filePath} — File metadata, content download, or verify
        app.MapMethods("/api/buckets/{id}/files/{*filePath}", new[] { "GET", "HEAD" },
            async (string id, string filePath, HttpContext ctx,
            IFileService fileService, FileStorageService storageService, IBucketService bucketService) =>
        {
            var bucket = await bucketService.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            if (filePath.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
            {
                var actualPath = filePath[..^"/content".Length];
                var contentStorageService = ctx.RequestServices.GetRequiredService<ContentStorageService>();
                return await ServeFileContent(id, actualPath, ctx, fileService, storageService, contentStorageService);
            }

            if (filePath.EndsWith("/verify", StringComparison.OrdinalIgnoreCase))
            {
                var actualPath = filePath[..^"/verify".Length];
                var verifyResult = await fileService.VerifyAsync(id, actualPath);
                return verifyResult == null ? ApiResults.NotFound("File not found") : Results.Ok(verifyResult);
            }

            var meta = await fileService.GetMetadataAsync(id, filePath);
            return meta != null
                ? Results.Ok(meta)
                : ApiResults.NotFound("File not found");
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
            var bucket = await bucketService.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            if (ctx.RequireAuth(out var auth) is { } err) return err;

            var deleted = await fileService.DeleteAsync(id, filePath, auth);
            if (deleted)
            {
                logger.LogInformation("File {FilePath} deleted from bucket {BucketId}", filePath, id);
                return Results.NoContent();
            }
            return ApiResults.NotFound("File not found");
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
            if (!filePath.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();

            var actualPath = filePath[..^"/content".Length];

            var bucket = await bucketService.GetBucketAsync(id);
            if (bucket == null)
                return ApiResults.NotFound("Bucket not found");

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return ApiResults.Forbidden("Authentication required", "Use an API key, admin key, or upload token.");

                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return ApiResults.Forbidden("Invalid or expired upload token");

                auth = AuthContext.Admin();
            }

            var meta = await fileService.GetMetadataAsync(id, actualPath);
            if (meta == null)
                return ApiResults.Error("File not found", 404, "Use upload endpoints to create files.");

            var isAppend = ctx.Request.Headers["X-Append"].FirstOrDefault()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            long offset = 0;
            if (!isAppend)
            {
                var contentRange = ctx.Request.Headers.ContentRange.FirstOrDefault();
                if (contentRange == null || !contentRange.StartsWith("bytes "))
                    return ApiResults.BadRequest("Content-Range header required for non-append PATCH");

                var rangePart = contentRange["bytes ".Length..];
                var slashIndex = rangePart.IndexOf('/');
                if (slashIndex > 0)
                    rangePart = rangePart[..slashIndex];

                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex < 0 || !long.TryParse(rangePart[..dashIndex], out offset))
                    return ApiResults.BadRequest("Invalid Content-Range");

                if (offset < 0 || offset > meta.Size)
                    return ApiResults.Error("Range not satisfiable", 416);
            }

            var patched = await fileService.PatchFileAsync(id, actualPath, ctx.Request.Body, offset, isAppend);
            if (!patched)
                return Results.NotFound();

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
        IFileService fileService, FileStorageService storageService, ContentStorageService contentStorageService)
    {
        var meta = await fileService.GetMetadataAsync(bucketId, path);
        if (meta == null)
            return ApiResults.NotFound("File not found");

        string physicalPath;
        string etag;
        if (meta.Sha256 != null)
        {
            var diskPath = await fileService.GetContentDiskPathAsync(bucketId, path);
            if (diskPath == null) return ApiResults.NotFound("File not found");
            physicalPath = contentStorageService.GetFullPath(diskPath);
            etag = $"\"{meta.Sha256}\"";
        }
        else
        {
            physicalPath = storageService.GetFilePath(bucketId, path);
            etag = $"\"{meta.Size}-{meta.UpdatedAt.Ticks}\"";
        }

        var lastModified = meta.UpdatedAt;

        if (ctx.Request.Headers.IfNoneMatch.FirstOrDefault() == etag)
            return Results.StatusCode(304);

        if (ctx.Request.Headers.IfModifiedSince.Count > 0)
        {
            if (DateTimeOffset.TryParse(ctx.Request.Headers.IfModifiedSince, out var ifModifiedSince))
            {
                if (lastModified <= ifModifiedSince.UtcDateTime.AddSeconds(1))
                    return Results.StatusCode(304);
            }
        }

        if (!System.IO.File.Exists(physicalPath))
            return ApiResults.NotFound("File not found");

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

        return TypedResults.PhysicalFile(
            physicalPath,
            contentType,
            fileDownloadName: fileDownloadName,
            lastModified: lastModifiedOffset,
            entityTag: etagValue,
            enableRangeProcessing: true);
    }
}
