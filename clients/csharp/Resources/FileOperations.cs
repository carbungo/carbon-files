using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class FileOperations
{
    private readonly HttpTransport _transport;
    private readonly string _bucketId;

    internal FileOperations(HttpTransport transport, string bucketId)
    {
        _transport = transport;
        _bucketId = bucketId;
    }
}
