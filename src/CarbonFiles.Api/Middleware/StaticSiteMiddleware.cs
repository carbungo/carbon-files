using CarbonFiles.Api.Endpoints;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Api.Middleware;

public sealed class StaticSiteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _siteDomain;

    public StaticSiteMiddleware(RequestDelegate next, IOptions<CarbonFilesOptions> options)
    {
        _next = next;
        _siteDomain = options.Value.SiteDomain?.Trim().Trim('.');
        if (string.IsNullOrEmpty(_siteDomain))
            _siteDomain = null;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryGetBucketId(context.Request.Host.Host, out var bucketId))
        {
            await _next(context);
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";
            return;
        }

        var bucketService = context.RequestServices.GetRequiredService<IBucketService>();
        var fileService = context.RequestServices.GetRequiredService<IFileService>();
        var storageService = context.RequestServices.GetRequiredService<FileStorageService>();
        var contentStorageService = context.RequestServices.GetRequiredService<ContentStorageService>();

        var bucket = await bucketService.GetBucketAsync(bucketId, ignoreCase: true);
        if (bucket == null)
        {
            await WriteNotFoundAsync(context);
            return;
        }
        bucketId = bucket.Id;

        var requestPath = (context.Request.Path.Value ?? string.Empty).Trim('/');
        if (requestPath.Length == 0)
        {
            if (!await FileExistsAsync(bucketId, "index.html", fileService))
            {
                await WriteNotFoundAsync(context);
                return;
            }

            await ServeAsync(bucketId, "index.html", context, fileService, storageService, contentStorageService);
            return;
        }

        var resolvedPath = await ResolvePathAsync(bucketId, requestPath, fileService);
        if (resolvedPath != null)
        {
            await ServeAsync(bucketId, resolvedPath, context, fileService, storageService, contentStorageService);
            return;
        }

        if (bucket.SpaMode)
        {
            if (await FileExistsAsync(bucketId, "index.html", fileService))
            {
                await ServeAsync(bucketId, "index.html", context, fileService, storageService, contentStorageService);
                return;
            }
        }
        else if (await FileExistsAsync(bucketId, "404.html", fileService))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await ServeAsync(bucketId, "404.html", context, fileService, storageService, contentStorageService);
            return;
        }

        await WriteNotFoundAsync(context);
    }

    private bool TryGetBucketId(string host, out string bucketId)
    {
        bucketId = string.Empty;
        if (_siteDomain == null)
            return false;

        host = host.TrimEnd('.');
        var suffix = "." + _siteDomain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || host.Length == suffix.Length)
            return false;

        bucketId = host[..^suffix.Length];
        return bucketId.Length > 0;
    }

    private static async Task<string?> ResolvePathAsync(string bucketId, string requestPath,
        IFileService fileService)
    {
        if (await FileExistsAsync(bucketId, requestPath, fileService))
            return requestPath;

        if (System.IO.Path.HasExtension(requestPath))
            return null;

        var htmlPath = requestPath + ".html";
        if (await FileExistsAsync(bucketId, htmlPath, fileService))
            return htmlPath;

        var indexPath = requestPath + "/index.html";
        return await FileExistsAsync(bucketId, indexPath, fileService) ? indexPath : null;
    }

    private static async Task<bool> FileExistsAsync(string bucketId, string path, IFileService fileService)
        => await fileService.GetMetadataAsync(bucketId, path) != null;

    private static async Task ServeAsync(string bucketId, string path, HttpContext context,
        IFileService fileService, FileStorageService storageService,
        ContentStorageService contentStorageService)
    {
        var result = await FileEndpoints.ServeFileContent(
            bucketId, path, context, fileService, storageService, contentStorageService);
        await result.ExecuteAsync(context);
    }

    private static async Task WriteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.WriteAsync("Not Found", context.RequestAborted);
    }
}
