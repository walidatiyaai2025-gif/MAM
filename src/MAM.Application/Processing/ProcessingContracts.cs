namespace MAM.Application.Processing;

public enum ProcessingJobState : byte
{
    Queued = 0,
    Leased = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed record ProcessingProfileDescriptor(
    string Id,
    int Version,
    string MediaKind,
    bool GeneratesDerivative,
    string? OutputExtension,
    string? ContentType,
    string Strategy);

public static class BuiltInProcessingProfiles
{
    public const string Inspect = "inspect-v1";
    public const string VideoProxy = "video-proxy-v1";
    public const string ImagePreview = "image-preview-v1";
    public const string AudioPreview = "audio-preview-v1";
    public const string PdfInline = "pdf-inline-v1";
    public const string OcrText = "ocr-text-v1";

    public static IReadOnlyList<ProcessingProfileDescriptor> All { get; } = new[]
    {
        new ProcessingProfileDescriptor(Inspect, 1, "Any", false, null, null, "FFprobe technical inspection only"),
        new ProcessingProfileDescriptor(VideoProxy, 1, "Video", true, ".mp4", "video/mp4", "H.264/AAC web proxy"),
        new ProcessingProfileDescriptor(ImagePreview, 1, "Image", true, ".jpg", "image/jpeg", "JPEG preview, max width 1280"),
        new ProcessingProfileDescriptor(AudioPreview, 1, "Audio", true, ".m4a", "audio/mp4", "AAC audio preview"),
        new ProcessingProfileDescriptor(PdfInline, 1, "Document", false, null, "application/pdf", "Server-streamed original PDF preview"),
        new ProcessingProfileDescriptor(OcrText, 1, "Image/Document", true, ".txt", "text/plain; charset=utf-8", "Arabic/English OCR text extraction (Tesseract)")
    };

    public static ProcessingProfileDescriptor? Find(string id) =>
        All.FirstOrDefault(profile => string.Equals(profile.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record ProcessingJobSnapshot(
    Guid JobId,
    Guid AssetId,
    string ProfileId,
    int ProfileVersion,
    ProcessingJobState State,
    int AttemptCount,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record TechnicalMetadataSnapshot(
    Guid AssetId,
    string MediaType,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? AudioCodec,
    int? AudioChannels,
    int? AudioSampleRate,
    string RawJson,
    DateTimeOffset InspectedAtUtc);

public sealed record DerivativeSnapshot(
    Guid DerivativeId,
    Guid AssetId,
    string ProfileId,
    int ProfileVersion,
    string ObjectKey,
    string ContentType,
    long Length,
    string Sha256,
    DateTimeOffset CreatedAtUtc);

public sealed record ProcessingPreviewPayload(
    Stream Content,
    string ContentType,
    string FileName,
    long Length,
    string Sha256);

public sealed record ProcessingHealth(bool IsReady, string Provider, string Detail);

public interface IMediaProcessingService
{
    IReadOnlyList<ProcessingProfileDescriptor> Profiles { get; }
    Task<ProcessingJobSnapshot> EnqueueAsync(Guid assetId, string profileId, string actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessingJobSnapshot>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<ProcessingJobSnapshot> RetryAsync(Guid jobId, string actorId, CancellationToken cancellationToken = default);
    Task<ProcessingJobSnapshot?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default);
    Task HeartbeatAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default);
    Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default);
    Task<TechnicalMetadataSnapshot?> GetTechnicalMetadataAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DerivativeSnapshot>> ListDerivativesAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<ProcessingPreviewPayload?> OpenDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken = default);
    Task<ProcessingPreviewPayload?> OpenOriginalPreviewAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<ProcessingHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class ProcessingRequestException : Exception
{
    public ProcessingRequestException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
