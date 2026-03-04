using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class ShortUrlOperations
{
    private readonly HttpTransport _transport;
    internal ShortUrlOperations(HttpTransport transport) => _transport = transport;
}
