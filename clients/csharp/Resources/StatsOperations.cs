using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class StatsOperations
{
    private readonly HttpTransport _transport;
    internal StatsOperations(HttpTransport transport) => _transport = transport;
}
