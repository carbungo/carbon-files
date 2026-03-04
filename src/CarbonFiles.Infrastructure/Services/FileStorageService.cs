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
    public async Task<long> StoreAsync(string bucketId, string filePath, Stream content, long maxSize = 0, CancellationToken ct = default)
    {
        var targetPath = GetFilePath(bucketId, filePath);
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);

        var tempPath = $"{targetPath}.tmp.{Guid.NewGuid():N}";

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 1024 * 1024,    // Pause network reads when 1MB buffered
            resumeWriterThreshold: 512 * 1024,     // Resume when buffer drops to 512KB
            minimumSegmentSize: 128 * 1024,        // 128KB segments
            useSynchronizationContext: false));

        var fillTask = FillPipeFromStreamAsync(pipe.Writer, content, maxSize, ct);
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
            // Surface the root-cause exception from the fill task (e.g. FileTooLargeException)
            if (fillTask.IsFaulted)
                throw fillTask.Exception!.InnerException!;
            throw;
        }

        File.Move(tempPath, targetPath, overwrite: true);
        _logger.LogDebug("Stored {Size} bytes to {Path}", totalBytes, targetPath);
        return totalBytes;
    }

    /// <summary>
    /// Network → Pipe: reads from the source stream into the pipe writer.
    /// Returns total bytes read. Throws FileTooLargeException if maxSize exceeded.
    /// </summary>
    private static async Task<long> FillPipeFromStreamAsync(PipeWriter writer, Stream source, long maxSize, CancellationToken ct)
    {
        const int MinimumReadSize = 128 * 1024; // 128KB read chunks
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

    public async Task<long> PatchFileAsync(string bucketId, string filePath, Stream content, long offset, bool append)
    {
        var path = GetFilePath(bucketId, filePath);
        if (!File.Exists(path))
            return -1;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 81920);

        if (append)
        {
            fs.Seek(0, SeekOrigin.End);
        }
        else
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        await content.CopyToAsync(fs);

        _logger.LogDebug("Patched file at {Path} (append={Append}, offset={Offset}, new size={NewSize})", path, append, offset, fs.Length);

        return fs.Length;
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
