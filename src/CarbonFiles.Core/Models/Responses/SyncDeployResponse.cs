namespace CarbonFiles.Core.Models.Responses;

public sealed class SyncDeployResponse
{
    public required int Kept { get; init; }
    public required int Deleted { get; init; }
}
