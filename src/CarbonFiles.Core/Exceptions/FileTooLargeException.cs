namespace CarbonFiles.Core.Exceptions;

public sealed class FileTooLargeException : Exception
{
    public long MaxSize { get; }

    public FileTooLargeException(long maxSize)
        : base($"File exceeds maximum upload size of {maxSize} bytes")
    {
        MaxSize = maxSize;
    }
}
