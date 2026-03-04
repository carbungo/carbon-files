using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data.Entities;

namespace CarbonFiles.Infrastructure.Data;

public static class EntityMapping
{
    public static Bucket ToBucket(this BucketEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Owner = entity.Owner,
        Description = entity.Description,
        CreatedAt = entity.CreatedAt,
        ExpiresAt = entity.ExpiresAt,
        LastUsedAt = entity.LastUsedAt,
        FileCount = entity.FileCount,
        TotalSize = entity.TotalSize
    };

    public static BucketFile ToBucketFile(this FileEntity entity) => new()
    {
        Path = entity.Path,
        Name = entity.Name,
        Size = entity.Size,
        MimeType = entity.MimeType,
        ShortCode = entity.ShortCode,
        ShortUrl = entity.ShortCode != null ? $"/s/{entity.ShortCode}" : null,
        Sha256 = entity.ContentHash,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    public static ApiKeyListItem ToListItem(this ApiKeyEntity entity, int bucketCount = 0, int fileCount = 0, long totalSize = 0) => new()
    {
        Prefix = entity.Prefix,
        Name = entity.Name,
        CreatedAt = entity.CreatedAt,
        LastUsedAt = entity.LastUsedAt,
        BucketCount = bucketCount,
        FileCount = fileCount,
        TotalSize = totalSize
    };
}
