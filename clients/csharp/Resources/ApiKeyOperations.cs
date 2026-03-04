using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class ApiKeyOperations
{
    private readonly HttpTransport _transport;
    internal ApiKeyOperations(HttpTransport transport) => _transport = transport;
}
