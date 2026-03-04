namespace CarbonFiles.Core.Models.Responses;

public sealed class DirectoryListingResponse
{
    public required IReadOnlyList<BucketFile> Files { get; init; }
    public required IReadOnlyList<string> Folders { get; init; }
    public int TotalFiles { get; init; }
    public int TotalFolders { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}
