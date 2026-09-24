namespace CarbonFiles.Core.Models.Responses;

public sealed class BucketChanges
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool? SpaMode { get; init; }
}
