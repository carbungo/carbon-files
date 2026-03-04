using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class DashboardOperations
{
    private readonly HttpTransport _transport;
    internal DashboardOperations(HttpTransport transport) => _transport = transport;
}
