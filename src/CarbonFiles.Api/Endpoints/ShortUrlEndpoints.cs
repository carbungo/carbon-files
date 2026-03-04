using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;

namespace CarbonFiles.Api.Endpoints;

public static class ShortUrlEndpoints
{
    public static void MapShortUrlEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /s/{code} — Resolve short URL (public)
        app.MapGet("/s/{code}", async (string code, IShortUrlService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.ShortUrlEndpoints");
            var url = await svc.ResolveAsync(code);
            if (url != null)
            {
                logger.LogDebug("Short URL {Code} resolved", code);
                return Results.Redirect(url);
            }
            return Results.NotFound();
        })
        .Produces(302)
        .Produces(404)
        .WithTags("Short URLs")
        .WithSummary("Resolve short URL")
        .WithDescription("Public. Redirects (302) to the file content URL for the given short code.");

        // DELETE /api/short/{code} — Delete short URL (owner or admin)
        app.MapDelete("/api/short/{code}", async (string code, HttpContext ctx, IShortUrlService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.ShortUrlEndpoints");
            if (ctx.RequireAuth(out var auth) is { } err) return err;

            var deleted = await svc.DeleteAsync(code, auth);
            if (deleted)
            {
                logger.LogInformation("Short URL {Code} deleted", code);
                return Results.NoContent();
            }
            return Results.NotFound();
        })
        .Produces(204)
        .Produces<ErrorResponse>(403)
        .Produces(404)
        .WithTags("Short URLs")
        .WithSummary("Delete short URL")
        .WithDescription("Auth: Bucket owner or admin. Deletes a short URL by its code.");
    }
}
