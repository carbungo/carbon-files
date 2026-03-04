using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Exceptions;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileStorageService _sut;

    public FileStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_fss_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Create a bucket directory
        Directory.CreateDirectory(Path.Combine(_tempDir, "bucket01"));

        var options = Options.Create(new CarbonFilesOptions { DataDir = _tempDir });
        _sut = new FileStorageService(options, NullLogger<FileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Basic pipeline behavior ─────────────────────────────────────────

    [Fact]
    public async Task StoreAsync_SmallFile_WritesCorrectBytes()
    {
        var data = "Hello, pipelined world!"u8.ToArray();
        using var stream = new MemoryStream(data);

        var result = await _sut.StoreAsync("bucket01", "small.txt", stream, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(data.Length);

        var stored = await File.ReadAllBytesAsync(_sut.GetFilePath("bucket01", "small.txt"), TestContext.Current.CancellationToken);
        stored.Should().Equal(data);
    }

    [Fact]
    public async Task StoreAsync_EmptyStream_WritesEmptyFile()
    {
        using var stream = new MemoryStream([]);

        var result = await _sut.StoreAsync("bucket01", "empty.txt", stream, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(0);
        var stored = await File.ReadAllBytesAsync(_sut.GetFilePath("bucket01", "empty.txt"), TestContext.Current.CancellationToken);
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_LargeData_ExercisesBackpressure()
    {
        // 4MB — larger than the 1MB pause threshold, so backpressure kicks in
        var data = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var result = await _sut.StoreAsync("bucket01", "large.bin", stream, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(data.Length);

        var stored = await File.ReadAllBytesAsync(_sut.GetFilePath("bucket01", "large.bin"), TestContext.Current.CancellationToken);
        stored.Should().Equal(data);
    }

    [Fact]
    public async Task StoreAsync_Overwrite_ReplacesExistingFile()
    {
        using var stream1 = new MemoryStream("version 1"u8.ToArray());
        await _sut.StoreAsync("bucket01", "overwrite.txt", stream1, ct: TestContext.Current.CancellationToken);

        using var stream2 = new MemoryStream("version 2 is longer"u8.ToArray());
        var result = await _sut.StoreAsync("bucket01", "overwrite.txt", stream2, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be("version 2 is longer"u8.ToArray().Length);
        var stored = await File.ReadAllBytesAsync(_sut.GetFilePath("bucket01", "overwrite.txt"), TestContext.Current.CancellationToken);
        stored.Should().Equal("version 2 is longer"u8.ToArray());
    }

    // ── MaxSize enforcement ─────────────────────────────────────────────

    [Fact]
    public async Task StoreAsync_MaxSizeExceeded_ThrowsFileTooLargeException()
    {
        var data = new byte[1024]; // 1KB
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var act = () => _sut.StoreAsync("bucket01", "toobig.bin", stream, maxSize: 512, ct: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FileTooLargeException>()
            .Where(ex => ex.MaxSize == 512);
    }

    [Fact]
    public async Task StoreAsync_MaxSizeExceeded_DoesNotLeaveFile()
    {
        var data = new byte[1024];
        using var stream = new MemoryStream(data);

        try
        {
            await _sut.StoreAsync("bucket01", "ghost.bin", stream, maxSize: 100, ct: TestContext.Current.CancellationToken);
        }
        catch (FileTooLargeException) { }

        _sut.FileExists("bucket01", "ghost.bin").Should().BeFalse();
    }

    [Fact]
    public async Task StoreAsync_MaxSizeExceeded_LargeStream_ThrowsFileTooLargeException()
    {
        // Data larger than pipe buffer (>1MB) to ensure exception fires mid-stream
        var data = new byte[2 * 1024 * 1024]; // 2MB
        using var stream = new MemoryStream(data);

        var act = () => _sut.StoreAsync("bucket01", "huge.bin", stream, maxSize: 1024 * 1024, ct: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FileTooLargeException>();
    }

    [Fact]
    public async Task StoreAsync_ExactlyAtMaxSize_Succeeds()
    {
        var data = new byte[512];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var result = await _sut.StoreAsync("bucket01", "exact.bin", stream, maxSize: 512, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(512);
    }

    [Fact]
    public async Task StoreAsync_WithinMaxSize_Succeeds()
    {
        var data = new byte[256];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var result = await _sut.StoreAsync("bucket01", "small.bin", stream, maxSize: 1024, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(256);
    }

    [Fact]
    public async Task StoreAsync_MaxSizeZero_MeansUnlimited()
    {
        var data = new byte[2 * 1024 * 1024]; // 2MB with maxSize=0 (unlimited)
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var result = await _sut.StoreAsync("bucket01", "unlimited.bin", stream, maxSize: 0, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(data.Length);
    }

    // ── Cancellation ────────────────────────────────────────────────────

    [Fact]
    public async Task StoreAsync_Cancellation_ThrowsAndCleansUp()
    {
        using var cts = new CancellationTokenSource();
        var slowStream = new SlowStream(totalBytes: 10 * 1024 * 1024);
        // Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = () => _sut.StoreAsync("bucket01", "cancelled.bin", slowStream, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _sut.FileExists("bucket01", "cancelled.bin").Should().BeFalse();
    }

    // ── Concurrency verification ────────────────────────────────────────

    [Fact]
    public async Task StoreAsync_ConcurrentReadWrite_DoesNotDeadlock()
    {
        // Use a stream that yields data in small chunks with delays to simulate network
        var data = new byte[512 * 1024]; // 512KB
        Random.Shared.NextBytes(data);
        var stream = new ChunkedStream(data, chunkSize: 4096);

        var result = await _sut.StoreAsync("bucket01", "chunked.bin", stream, ct: TestContext.Current.CancellationToken);

        result.Size.Should().Be(data.Length);
        var stored = await File.ReadAllBytesAsync(_sut.GetFilePath("bucket01", "chunked.bin"), TestContext.Current.CancellationToken);
        stored.Should().Equal(data);
    }

    // ── Test helpers ────────────────────────────────────────────────────

    /// <summary>
    /// A stream that delivers data slowly, one small chunk at a time,
    /// and supports cancellation to verify the pipeline handles it.
    /// </summary>
    private sealed class SlowStream(long totalBytes) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => totalBytes;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            var toRead = (int)Math.Min(buffer.Length, Math.Min(1024, totalBytes - _position));
            if (toRead <= 0) return 0;
            buffer[..toRead].Span.Fill(0xAB);
            _position += toRead;
            return toRead;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Delivers data in fixed-size chunks to simulate network fragmentation.
    /// </summary>
    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = data.Length - _position;
            if (remaining <= 0) return ValueTask.FromResult(0);
            var toRead = Math.Min(buffer.Length, Math.Min(chunkSize, remaining));
            data.AsMemory(_position, toRead).CopyTo(buffer);
            _position += toRead;
            return ValueTask.FromResult(toRead);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
