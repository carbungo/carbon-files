using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Models.Responses;

public sealed class BucketDetailResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
    public int UniqueContentCount { get; init; }
    public long UniqueContentSize { get; init; }
    public IReadOnlyList<BucketFile>? Files { get; init; }
    public bool? HasMoreFiles { get; init; }
}
