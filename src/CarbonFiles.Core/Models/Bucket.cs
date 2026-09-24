namespace CarbonFiles.Core.Models;

public sealed class Bucket
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Owner { get; init; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool SpaMode { get; set; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
}
