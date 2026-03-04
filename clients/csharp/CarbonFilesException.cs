using System.Net;

namespace CarbonFiles.Client;

public class CarbonFilesException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Error { get; }
    public string? Hint { get; }

    public CarbonFilesException(HttpStatusCode statusCode, string error, string? hint = null)
        : base(hint != null ? $"{error} ({hint})" : error)
    {
        StatusCode = statusCode;
        Error = error;
        Hint = hint;
    }
}
