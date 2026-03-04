using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class BucketOperations
{
    private readonly HttpTransport _transport;
    internal BucketOperations(HttpTransport transport) => _transport = transport;
}
