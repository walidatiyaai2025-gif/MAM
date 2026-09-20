namespace MAM.Application.Tapes;

public sealed record TapeAttachmentItem(
    Guid AttachmentId,
    Guid TapeId,
    Guid AssetId,
    string DisplayName,
    string OriginalFileName,
    long Length,
    string? MediaKind,
    string? OcrState,
    int OcrProgressPercent,
    string? OcrDetail,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

public sealed record LinkTapeAttachmentRequest(
    Guid AssetId,
    string? DisplayName);

public interface ITapeAttachmentService
{
    Task<IReadOnlyList<TapeAttachmentItem>> ListAsync(Guid tapeId, CancellationToken cancellationToken = default);
    Task<TapeAttachmentItem> LinkAsync(Guid tapeId, LinkTapeAttachmentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid tapeId, Guid attachmentId, string actorId, CancellationToken cancellationToken = default);
}

public static class TapeAttachmentPolicy
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",".jpg",".jpeg",".png",".tif",".tiff",".bmp",
        ".doc",".docx",".rtf",".txt",".odt"
    };

    public static bool SupportsOcr(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && SupportedExtensions.Contains(Path.GetExtension(fileName));

    public static string AcceptedExtensions =>
        ".pdf,.jpg,.jpeg,.png,.tif,.tiff,.bmp,.doc,.docx,.rtf,.txt,.odt";
}
