namespace CarbonFiles.Core.Models.Responses;

public sealed class UploadResponse
{
    public required IReadOnlyList<UploadedFile> Uploaded { get; init; }
}
