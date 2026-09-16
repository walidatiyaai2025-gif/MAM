using System.Security.Cryptography;
using System.Text;

namespace MAM.Application.Discovery;

public static class VisualStates
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Unavailable = "Unavailable";
    public const string Failed = "Failed";
}

public sealed record VisualEmbeddingDescriptor(
    string Provider,
    string ModelId,
    int ModelVersion,
    int Dimensions,
    IReadOnlyList<float> Values);

public sealed record VisualProviderHealth(bool IsReady, string Provider, string ModelId, int ModelVersion, int Dimensions, string Detail);

public interface IVisualEmbeddingProvider
{
    VisualProviderHealth Health { get; }
    Task<VisualEmbeddingDescriptor> EmbedAsync(Stream content, string? fileName, string? contentType, CancellationToken cancellationToken = default);
}

public sealed record VisualSegmentSnapshot(
    Guid SegmentId,
    Guid AssetId,
    string SourceKind,
    int SegmentIndex,
    long? StartMs,
    long? EndMs,
    int? PageNumber,
    string Text,
    long? CaptureMs,
    string VisualState,
    bool HasThumbnail,
    string? ThumbnailContentType,
    long? ThumbnailLength,
    string? ThumbnailSha256,
    string? LastError,
    DateTimeOffset? VisualUpdatedAtUtc);

public sealed record VisualSearchHit(
    Guid AssetId,
    string Title,
    string MediaKind,
    Guid? SegmentId,
    string SourceKind,
    int? SegmentIndex,
    long? StartMs,
    long? EndMs,
    int? PageNumber,
    double Score,
    bool HasThumbnail,
    string Provider,
    string ModelId,
    int ModelVersion,
    DateTimeOffset UpdatedAtUtc);

public sealed record VisualSearchResult(
    IReadOnlyList<VisualSearchHit> Items,
    string Provider,
    string ModelId,
    int ModelVersion,
    int Dimensions,
    int RequestedLimit);

public sealed record VisualThumbnailPayload(Stream Content, string ContentType, string FileName, long Length, string Sha256);
public sealed record VisualSearchHealth(bool IsReady, string Provider, string ModelId, int ModelVersion, int Dimensions, string Detail);

public interface IVisualSearchService
{
    Task<VisualSearchHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VisualSegmentSnapshot>> ListSegmentsAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default);
    Task RegisterSegmentsAsync(Guid assetId, string sourceKind, IReadOnlyList<TextSegmentSnapshot> segments, bool visualEligible, string? unavailableReason, CancellationToken cancellationToken = default);
    Task<VisualSegmentSnapshot> UpsertSegmentThumbnailAsync(Guid assetId, string sourceKind, int segmentIndex, long captureMs, Stream thumbnail, string contentType, CancellationToken cancellationToken = default);
    Task IndexAssetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<VisualThumbnailPayload?> OpenThumbnailAsync(Guid assetId, Guid segmentId, CancellationToken cancellationToken = default);
    Task<VisualSearchResult> SearchAsync(Stream queryImage, string? fileName, string? contentType, int limit = 30, CancellationToken cancellationToken = default);
}

public static class VisualSegmentIdentity
{
    public static Guid Create(Guid assetId, string sourceKind, int segmentIndex)
    {
        var normalized = (sourceKind ?? string.Empty).Trim().ToLowerInvariant();
        var input = Encoding.UTF8.GetBytes($"{assetId:D}|{normalized}|{segmentIndex}");
        var hash = SHA256.HashData(input);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}

public sealed class VisualSearchRequestException : Exception
{
    public VisualSearchRequestException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}