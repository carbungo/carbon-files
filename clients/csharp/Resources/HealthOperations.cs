using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class HealthOperations
{
    private readonly HttpTransport _transport;
    internal HealthOperations(HttpTransport transport) => _transport = transport;

    public Task<HealthResponse> CheckAsync(CancellationToken ct = default)
        => _transport.GetAsync<HealthResponse>("/healthz", ct);
}
