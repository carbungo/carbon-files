namespace CarbonFiles.Core.Models.Responses;

public sealed class VerifyResponse
{
    public required string Path { get; init; }
    public required string StoredHash { get; init; }
    public required string ComputedHash { get; init; }
    public bool Valid { get; init; }
}
