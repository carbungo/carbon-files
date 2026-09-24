namespace CarbonFiles.Core.Models.Requests;

public sealed class UpdateBucketRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ExpiresIn { get; init; }
    public bool? SpaMode { get; init; }
}
