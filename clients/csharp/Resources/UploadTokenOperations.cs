using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class UploadTokenOperations
{
    private readonly HttpTransport _transport;
    private readonly string _bucketId;

    internal UploadTokenOperations(HttpTransport transport, string bucketId)
    {
        _transport = transport;
        _bucketId = bucketId;
    }
}
