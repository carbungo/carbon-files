namespace CarbonFiles.Core.Models.Requests;

public sealed class SyncDeployRequest
{
    public required List<DeployFile> Files { get; init; }
}

public sealed class DeployFile
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
}
