using System.IO.Pipelines;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class FileStorageService
{
    private readonly string _dataDir;
    private readonly ILogger<FileStorageService> _logger;

    public sealed record StoreResult(long Size, string Sha256Hash);

    public FileStorageService(IOptions<CarbonFilesOptions> options, ILogger<FileStorageService> logger)
    {
        _dataDir = options.Value.DataDir;
        _logger = logger;
    }

    public string GetFilePath(string bucketId, string filePath)
    {
        var encoded = Uri.EscapeDataString(filePath);
        return Path.Combine(_dataDir, bucketId, encoded);
    }

    /// <summary>
    /// Stores a file using System.IO.Pipelines so network reads and disk writes
    /// run concurrently. The pipe provides backpressure — if disk is slow, network
    /// reads pause automatically via the pause/resume thresholds.
    /// </summary>
    public async Task<StoreResult> StoreAsync(string bucketId, string filePath, Stream content, long maxSize = 0, CancellationToken ct = default)
    {
        var targetPath = GetFilePath(bucketId, filePath);
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);

        var tempPath = $"{targetPath}.tmp.{Guid.NewGuid():N}";

        using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            minimumSegmentSize: 128 * 1024,
            useSynchronizationContext: false));

        var fillTask = FillPipeFromStreamAsync(pipe.Writer, content, maxSize, sha256, ct);
        var drainTask = DrainPipeToFileAsync(pipe.Reader, tempPath, ct);

        long totalBytes;
        try
        {
            await Task.WhenAll(fillTask, drainTask);
            totalBytes = fillTask.Result;
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            if (fillTask.IsFaulted)
                throw fillTask.Exception!.InnerException!;
            throw;
        }

        var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());

        File.Move(tempPath, targetPath, overwrite: true);
        _logger.LogDebug("Stored {Size} bytes to {Path} (sha256={Hash})", totalBytes, targetPath, hashHex);
        return new StoreResult(totalBytes, hashHex);
    }

    /// <summary>
    /// Streams content to a temp file, computing SHA256 inline.
    /// Returns the temp file path, size, and hash. Caller is responsible for
    /// moving or deleting the temp file.
    /// </summary>
    public async Task<(string TempPath, long Size, string Sha256Hash)> StoreToTempAsync(
        Stream content, long maxSize = 0, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(_dataDir, "tmp");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tmp");

        using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            minimumSegmentSize: 128 * 1024,
            useSynchronizationContext: false));

        var fillTask = FillPipeFromStreamAsync(pipe.Writer, content, maxSize, sha256, ct);
        var drainTask = DrainPipeToFileAsync(pipe.Reader, tempPath, ct);

        long totalBytes;
        try
        {
            await Task.WhenAll(fillTask, drainTask);
            totalBytes = fillTask.Result;
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort */ }
            if (fillTask.IsFaulted)
                throw fillTask.Exception!.InnerException!;
            throw;
        }

        var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());
        _logger.LogDebug("Stored {Size} bytes to temp {Path} (sha256={Hash})", totalBytes, tempPath, hashHex);
        return (tempPath, totalBytes, hashHex);
    }

    /// <summary>
    /// Network → Pipe: reads from the source stream into the pipe writer.
    /// Returns total bytes read. Throws FileTooLargeException if maxSize exceeded.
    /// </summary>
    private static async Task<long> FillPipeFromStreamAsync(
        PipeWriter writer, Stream source, long maxSize,
        System.Security.Cryptography.IncrementalHash hash,
        CancellationToken ct)
    {
        const int MinimumReadSize = 128 * 1024;
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var memory = writer.GetMemory(MinimumReadSize);
                var bytesRead = await source.ReadAsync(memory, ct);
                if (bytesRead == 0)
                    break;

                totalBytes += bytesRead;
                if (maxSize > 0 && totalBytes > maxSize)
                    throw new FileTooLargeException(maxSize);

                hash.AppendData(memory.Span[..bytesRead]);
                writer.Advance(bytesRead);

                var flush = await writer.FlushAsync(ct);
                if (flush.IsCompleted)
                    break;
            }
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
            throw;
        }

        await writer.CompleteAsync();
        return totalBytes;
    }

    /// <summary>
    /// Pipe → Disk: drains the pipe reader into a file.
    /// </summary>
    private static async Task DrainPipeToFileAsync(PipeReader reader, string filePath, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                    await fs.WriteAsync(segment, ct);

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (Exception ex)
        {
            await reader.CompleteAsync(ex);
            throw;
        }

        await reader.CompleteAsync();
    }

    public FileStream? OpenRead(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920) : null;
    }

    public long GetFileSize(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path) ? new System.IO.FileInfo(path).Length : -1;
    }

    public bool FileExists(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path);
    }

    public void DeleteFile(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogDebug("Deleted file at {Path}", path);
        }
    }

    public void DeleteBucketDir(string bucketId)
    {
        var dir = Path.Combine(_dataDir, bucketId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
            _logger.LogDebug("Deleted bucket directory {Dir}", dir);
        }
    }
}
