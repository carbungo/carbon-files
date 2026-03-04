using CarbonFiles.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ContentStorageService
{
    private readonly string _contentDir;
    private readonly ILogger<ContentStorageService> _logger;

    public ContentStorageService(IOptions<CarbonFilesOptions> options, ILogger<ContentStorageService> logger)
    {
        _contentDir = Path.Combine(options.Value.DataDir, "content");
        _logger = logger;
    }

    /// <summary>
    /// Computes the relative disk path for a given SHA256 hash.
    /// Format: ab/cd/abcdef1234...
    /// </summary>
    public static string ComputeDiskPath(string hash)
    {
        return Path.Combine(hash[..2], hash[2..4], hash);
    }

    /// <summary>
    /// Returns the full absolute path for a content object.
    /// </summary>
    public string GetFullPath(string diskPath)
    {
        return Path.Combine(_contentDir, diskPath);
    }

    /// <summary>
    /// Moves a temp file into the content-addressed store.
    /// Creates the sharded directory structure if needed.
    /// </summary>
    public void MoveToContentStore(string tempPath, string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.Move(tempPath, fullPath, overwrite: false);
        _logger.LogDebug("Stored content at {Path}", fullPath);
    }

    /// <summary>
    /// Checks if a content file exists on disk.
    /// </summary>
    public bool Exists(string diskPath)
    {
        return File.Exists(GetFullPath(diskPath));
    }

    /// <summary>
    /// Deletes a content file from disk.
    /// </summary>
    public void Delete(string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogDebug("Deleted content at {Path}", fullPath);
        }
    }

    /// <summary>
    /// Opens a content file for reading.
    /// </summary>
    public FileStream? OpenRead(string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        return File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920)
            : null;
    }
}
