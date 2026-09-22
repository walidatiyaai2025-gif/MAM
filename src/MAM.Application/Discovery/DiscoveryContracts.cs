using System.Globalization;
using System.Text;

namespace MAM.Application.Discovery;

public static class MediaKinds
{
    public const string Video = "Video";
    public const string Audio = "Audio";
    public const string Image = "Image";
    public const string Document = "Document";
    public const string Other = "Other";

    public static string FromFileName(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".mp4" or ".mov" or ".mxf" or ".mkv" or ".avi" or ".webm" or ".m4v" => Video,
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".wma" => Audio,
            ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".webp" => Image,
            ".pdf" or ".doc" or ".docx" or ".rtf" or ".txt" or ".odt" => Document,
            _ => Other
        };
    }
}

public static class DiscoverySources
{
    public const string Metadata = "metadata";
    public const string Ocr = "ocr";
    public const string Transcript = "transcript";
    public const string Reference = "reference";
}

public sealed record CategorySnapshot(
    Guid CategoryId,
    Guid? ParentCategoryId,
    string NameEn,
    string? NameAr,
    bool IsSystem,
    int SortOrder,
    long Version,
    int AssetCount,
    int ChildCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateCategoryRequest(Guid? ParentCategoryId, string NameEn, string? NameAr, int SortOrder = 0);
public sealed record UpdateCategoryRequest(long ExpectedVersion, Guid? ParentCategoryId, string NameEn, string? NameAr, int SortOrder = 0);
public sealed record AssignAssetCategoryRequest(Guid? CategoryId);

public sealed record AssetCategorySnapshot(Guid AssetId, CategorySnapshot Category);

public sealed record TextSegmentSnapshot(
    int SegmentIndex,
    long? StartMs,
    long? EndMs,
    int? PageNumber,
    string Text);

public sealed record AssetTextSnapshot(
    Guid AssetId,
    string SourceKind,
    string? Language,
    string Text,
    string? ContentSha256,
    IReadOnlyList<TextSegmentSnapshot> Segments,
    DateTimeOffset UpdatedAtUtc);

public sealed record TextExtractionStatusSnapshot(
    Guid AssetId,
    string ExtractionKind,
    string State,
    int ProgressPercent,
    string? Detail,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record DiscoverySearchRequest(string Query, int Page = 1, int PageSize = 50, Guid? CategoryId = null, string? MediaKind = null);

public sealed record DiscoverySearchHit(
    Guid AssetId,
    string Title,
    string MediaKind,
    Guid CategoryId,
    string CategoryNameEn,
    string? CategoryNameAr,
    string MatchedSource,
    string Snippet,
    long? StartMs,
    long? EndMs,
    int? PageNumber,
    IReadOnlyList<string> ReferenceTags,
    DateTimeOffset UpdatedAtUtc);

public sealed record DiscoverySearchResult(IReadOnlyList<DiscoverySearchHit> Items, long TotalCount, int Page, int PageSize, string NormalizedQuery);

public sealed record ReferenceSubjectSnapshot(
    Guid SubjectId,
    string NameEn,
    string? NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    IReadOnlyList<string> Tags,
    bool IsActive,
    IReadOnlyList<Guid> ReferenceAssetIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateReferenceSubjectRequest(
    string NameEn,
    string? NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    IReadOnlyList<string>? Tags);

public sealed record UpdateReferenceSubjectRequest(
    string NameEn,
    string? NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    IReadOnlyList<string>? Tags,
    bool IsActive);

public sealed record ReferenceSubjectUsageSnapshot(Guid SubjectId, IReadOnlyList<Guid> TaggedAssetIds);

public sealed record AddReferenceImageRequest(Guid AssetId);
public sealed record AddAssetReferenceTagRequest(Guid SubjectId, decimal? Confidence = null, string DetectionSource = "manual");

public sealed record AssetReferenceTagSnapshot(
    Guid AssetId,
    Guid SubjectId,
    string NameEn,
    string? NameAr,
    decimal? Confidence,
    string DetectionSource,
    DateTimeOffset CreatedAtUtc);

public sealed record MediaPermissionSnapshot(
    string RoleName,
    string MediaKind,
    bool CanView,
    bool CanUpload,
    bool CanEdit,
    bool CanProcess,
    bool CanDownload,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertMediaPermissionRequest(
    string RoleName,
    string MediaKind,
    bool CanView,
    bool CanUpload,
    bool CanEdit,
    bool CanProcess,
    bool CanDownload);

public sealed record DiscoveryDashboardSnapshot(long CategoryCount, long UncategorizedAssetCount, long IndexedAssetCount, long TranscriptCount, long OcrCount, long ReferenceSubjectCount);

public sealed record DiscoveryHealth(bool IsReady, string Provider, string Detail);

public interface IDiscoveryService
{
    Task<DiscoveryHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategorySnapshot>> ListCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CategorySnapshot> CreateCategoryAsync(CreateCategoryRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<CategorySnapshot> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, string actorId, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(Guid categoryId, string actorId, CancellationToken cancellationToken = default);
    Task<AssetCategorySnapshot> GetAssetCategoryAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<AssetCategorySnapshot> AssignAssetCategoryAsync(Guid assetId, Guid? categoryId, string actorId, CancellationToken cancellationToken = default);

    Task UpsertTextAsync(Guid assetId, string sourceKind, string? language, string text, string? contentSha256, IReadOnlyList<TextSegmentSnapshot>? segments, CancellationToken cancellationToken = default);
    Task<AssetTextSnapshot?> GetTextAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default);
    Task SetExtractionStatusAsync(Guid assetId, string extractionKind, string state, int progressPercent, string? detail, bool completed, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TextExtractionStatusSnapshot>> GetExtractionStatusAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<DiscoverySearchResult> SearchAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReferenceSubjectSnapshot>> ListReferenceSubjectsAsync(CancellationToken cancellationToken = default);
    Task<ReferenceSubjectSnapshot> CreateReferenceSubjectAsync(CreateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<ReferenceSubjectSnapshot> UpdateReferenceSubjectAsync(Guid subjectId, UpdateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default);
    Task DeleteReferenceSubjectAsync(Guid subjectId, string actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceSubjectUsageSnapshot>> ListReferenceSubjectUsageAsync(CancellationToken cancellationToken = default);
    Task<ReferenceSubjectSnapshot> AddReferenceImageAsync(Guid subjectId, Guid assetId, string actorId, CancellationToken cancellationToken = default);
    Task<AssetReferenceTagSnapshot> AddAssetReferenceTagAsync(Guid assetId, AddAssetReferenceTagRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetReferenceTagSnapshot>> ListAssetReferenceTagsAsync(Guid assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaPermissionSnapshot>> ListMediaPermissionsAsync(CancellationToken cancellationToken = default);
    Task<MediaPermissionSnapshot> UpsertMediaPermissionAsync(UpsertMediaPermissionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<bool> IsMediaActionAllowedAsync(IEnumerable<string> roles, string mediaKind, string action, CancellationToken cancellationToken = default);
    Task<string> GetAssetMediaKindAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<DiscoveryDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
}

public static class DiscoveryText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            builder.Append(character switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                'ـ' => ' ',
                _ => char.IsLetterOrDigit(character) ? character : ' '
            });
        }
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyDictionary<string, int> Tokenize(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key.Length > 120 ? group.Key[..120] : group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class DiscoveryRequestException : Exception
{
    public DiscoveryRequestException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
