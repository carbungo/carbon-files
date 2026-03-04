using System.Text.Json;

namespace CarbonFiles.Client;

public class CarbonFilesClientOptions
{
    public Uri BaseAddress { get; set; } = null!;
    public string? ApiKey { get; set; }
    public HttpClient? HttpClient { get; set; }
    public JsonSerializerOptions? JsonOptions { get; set; }
}
