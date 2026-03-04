using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Resources;

namespace CarbonFiles.Client;

public class CarbonFilesClient
{
    private readonly HttpTransport _transport;

    public CarbonFilesClient(string baseAddress, string? apiKey = null)
        : this(new CarbonFilesClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            ApiKey = apiKey
        })
    {
    }

    public CarbonFilesClient(CarbonFilesClientOptions options)
    {
        var http = options.HttpClient ?? new HttpClient();
        if (http.BaseAddress == null && options.BaseAddress != null)
            http.BaseAddress = options.BaseAddress;

        _transport = new HttpTransport(http, options.ApiKey, options.JsonOptions);

        Health = new HealthOperations(_transport);
        Buckets = new BucketOperations(_transport);
        Keys = new ApiKeyOperations(_transport);
        Stats = new StatsOperations(_transport);
        ShortUrls = new ShortUrlOperations(_transport);
        Dashboard = new DashboardOperations(_transport);
    }

    public HealthOperations Health { get; }
    public BucketOperations Buckets { get; }
    public ApiKeyOperations Keys { get; }
    public StatsOperations Stats { get; }
    public ShortUrlOperations ShortUrls { get; }
    public DashboardOperations Dashboard { get; }
}
