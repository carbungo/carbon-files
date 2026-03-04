namespace CarbonFiles.Core.Models.Responses;

public sealed class FileTreeResponse
{
    public string? Prefix { get; init; }
    public required string Delimiter { get; init; }
    public required IReadOnlyList<DirectoryEntry> Directories { get; init; }
    public required IReadOnlyList<BucketFile> Files { get; init; }
    public int TotalFiles { get; init; }
    public int TotalDirectories { get; init; }
    public string? Cursor { get; init; }
}

public sealed class DirectoryEntry
{
    public required string Path { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
}
