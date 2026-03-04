using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Models;

namespace CarbonFiles.Api.Auth;

public static class AuthExtensions
{
    public static AuthContext GetAuthContext(this HttpContext context)
        => context.Items["AuthContext"] as AuthContext ?? AuthContext.Public();

    /// <summary>
    /// Returns an error result if the caller is unauthenticated (public), or null if authenticated.
    /// </summary>
    public static IResult? RequireAuth(this HttpContext ctx, out AuthContext auth)
    {
        auth = ctx.GetAuthContext();
        if (auth.IsPublic)
            return ApiResults.Forbidden("Authentication required", "Use an API key or admin key.");
        return null;
    }

    /// <summary>
    /// Returns an error result if the caller is not an admin, or null if admin.
    /// </summary>
    public static IResult? RequireAdmin(this HttpContext ctx, out AuthContext auth)
    {
        auth = ctx.GetAuthContext();
        if (!auth.IsAdmin)
            return ApiResults.Forbidden("Admin access required", "Use the admin key or a dashboard token.");
        return null;
    }
}

public static class ApiResults
{
    public static IResult Error(string message, int status, string? hint = null) =>
        Results.Json(
            new ErrorResponse { Error = message, Hint = hint },
            CarbonFilesJsonContext.Default.ErrorResponse,
            statusCode: status);

    public static IResult NotFound(string message = "Not found") => Error(message, 404);
    public static IResult Forbidden(string message, string? hint = null) => Error(message, 403, hint);
    public static IResult BadRequest(string message) => Error(message, 400);
}
