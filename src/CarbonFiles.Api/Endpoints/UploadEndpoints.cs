using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Exceptions;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CarbonFiles.Api.Endpoints;

public static class UploadEndpoints
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        { "file", "files", "upload", "uploads", "blob" };

    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/buckets/{id}/upload — Multipart upload (pipelined I/O)
        app.MapPost("/api/buckets/{id}/upload", async (string id, HttpContext ctx,
            IUploadService uploadService, IBucketService bucketService,
            IUploadTokenService uploadTokenService, IOptions<CarbonFilesOptions> options, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.UploadEndpoints");
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            string? validatedToken = null;
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key, admin key, or upload token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                // Validate upload token via service
                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return Results.Json(new ErrorResponse { Error = "Invalid or expired upload token" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                validatedToken = token;
                // Use admin auth context for upload token (it's authorized)
                auth = AuthContext.Admin();
            }

            // Parse multipart boundary from Content-Type
            if (!MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var mediaType) ||
                !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new ErrorResponse { Error = "Expected multipart/form-data" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
            if (string.IsNullOrEmpty(boundary))
                return Results.Json(new ErrorResponse { Error = "Missing multipart boundary" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            // Stream sections directly from the request body — no buffering
            var reader = new MultipartReader(boundary, ctx.Request.Body);
            var uploaded = new List<BucketFile>();
            var maxUploadSize = options.Value.MaxUploadSize;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ctx.RequestAborted)) != null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                    continue;

                // Skip non-file fields
                if (!contentDisposition.FileName.HasValue && !contentDisposition.FileNameStar.HasValue)
                    continue;

                var fieldName = contentDisposition.Name.HasValue
                    ? HeaderUtilities.RemoveQuotes(contentDisposition.Name).Value
                    : null;
                var fileName = contentDisposition.FileName.HasValue
                    ? HeaderUtilities.RemoveQuotes(contentDisposition.FileName).Value
                    : contentDisposition.FileNameStar.Value;

                // Determine path: custom field name takes precedence unless it's a generic name
                var path = GenericNames.Contains(fieldName ?? "") ? fileName : fieldName;

                if (string.IsNullOrWhiteSpace(path))
                    return Results.Json(new ErrorResponse { Error = "File path could not be determined" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

                try
                {
                    // Pipelined: network reads and disk writes run concurrently via System.IO.Pipelines
                    var result = await uploadService.StoreFileAsync(id, path, section.Body, auth, maxUploadSize, ctx.RequestAborted);
                    uploaded.Add(result);
                }
                catch (FileTooLargeException)
                {
                    return Results.Json(new ErrorResponse { Error = "File too large" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 413);
                }
            }

            // Update upload token usage if applicable
            if (validatedToken != null && uploaded.Count > 0)
            {
                await uploadTokenService.IncrementUsageAsync(validatedToken, uploaded.Count);
            }

            logger.LogInformation("Uploaded {FileCount} file(s) to bucket {BucketId}", uploaded.Count, id);
            return Results.Created($"/api/buckets/{id}/files", new UploadResponse { Uploaded = uploaded });
        })
        .Produces<UploadResponse>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(413)
        .DisableAntiforgery()
        .WithTags("Uploads")
        .WithSummary("Upload files (multipart)")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=). Upload one or more files via multipart/form-data. Field names become file paths unless generic (file, files, upload, etc.).");

        // PUT /api/buckets/{id}/upload/stream — Stream upload (single file, pipelined I/O)
        app.MapPut("/api/buckets/{id}/upload/stream", async (string id, HttpContext ctx,
            IUploadService uploadService, IBucketService bucketService,
            IUploadTokenService uploadTokenService, IOptions<CarbonFilesOptions> options, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.UploadEndpoints");
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            string? validatedToken = null;
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key, admin key, or upload token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                // Validate upload token via service
                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return Results.Json(new ErrorResponse { Error = "Invalid or expired upload token" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                validatedToken = token;
                auth = AuthContext.Admin();
            }

            var filename = ctx.Request.Query["filename"].FirstOrDefault();
            if (string.IsNullOrEmpty(filename))
                return Results.Json(new ErrorResponse { Error = "filename query parameter is required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            var maxUploadSize = options.Value.MaxUploadSize;

            BucketFile result;
            try
            {
                result = await uploadService.StoreFileAsync(id, filename, ctx.Request.Body, auth, maxUploadSize, ctx.RequestAborted);
            }
            catch (FileTooLargeException)
            {
                return Results.Json(new ErrorResponse { Error = "File too large" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 413);
            }

            logger.LogInformation("Stream uploaded {FileName} to bucket {BucketId}", filename, id);

            // Update upload token usage if applicable
            if (validatedToken != null)
            {
                await uploadTokenService.IncrementUsageAsync(validatedToken, 1);
            }

            return Results.Created($"/api/buckets/{id}/files/{result.Path}", new UploadResponse { Uploaded = [result] });
        })
        .Produces<UploadResponse>(201)
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(403)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(413)
        .WithTags("Uploads")
        .WithSummary("Upload file (streaming)")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=). Stream-upload a single file. Requires ?filename= query parameter.");
    }
}
