using System.Globalization;
using System.Text;

namespace MAM.Application.Curation;

public sealed record CurationSearchRequest(
    string? Query = null,
    string? Lifecycle = null,
    string? Category = null,
    string? Tag = null,
    Guid? CollectionId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record CurationFacetValue(string Value, long Count);

public sealed record CurationFacets(
    IReadOnlyList<CurationFacetValue> Lifecycles,
    IReadOnlyList<CurationFacetValue> Categories,
    IReadOnlyList<CurationFacetValue> Tags);

public sealed record CurationAssetItem(
    Guid Id,
    string Title,
    string? TitleAr,
    string Lifecycle,
    long Version,
    DateOnly? EventDate,
    string? Category,
    IReadOnlyList<string> Tags,
    string? PreservationNotes,
    DateTimeOffset UpdatedAtUtc,
    int CollectionCount);

public sealed record CurationSearchResult(
    IReadOnlyList<CurationAssetItem> Items,
    long TotalCount,
    int Page,
    int PageSize,
    CurationFacets Facets);

public sealed record AssetMetadataSnapshot(
    Guid AssetId,
    string SchemaKey,
    string TitleEn,
    string? TitleAr,
    DateOnly? EventDate,
    string? Category,
    IReadOnlyList<string> Tags,
    string? PreservationNotes,
    string Lifecycle,
    long Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record AssetMetadataUpdateRequest(
    long ExpectedVersion,
    string SchemaKey,
    string TitleEn,
    string? TitleAr,
    DateOnly? EventDate,
    string? Category,
    IReadOnlyList<string>? Tags,
    string? PreservationNotes);

public sealed record BulkMetadataItem(
    Guid AssetId,
    long ExpectedVersion,
    string SchemaKey,
    string TitleEn,
    string? TitleAr,
    DateOnly? EventDate,
    string? Category,
    IReadOnlyList<string>? Tags,
    string? PreservationNotes);

public sealed record BulkMetadataRequest(IReadOnlyList<BulkMetadataItem> Items);

public sealed record BulkMetadataItemResult(
    Guid AssetId,
    bool Succeeded,
    string Status,
    string? Error,
    AssetMetadataSnapshot? Current);

public sealed record BulkMetadataResult(
    int Requested,
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkMetadataItemResult> Results);

public sealed record CollectionSnapshot(
    Guid CollectionId,
    string NameEn,
    string? NameAr,
    long Version,
    int MemberCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateCollectionRequest(string NameEn, string? NameAr);
public sealed record CollectionMembershipRequest(long ExpectedVersion);
public sealed record LifecycleMutationRequest(long ExpectedVersion);

public sealed record CurationPolicy(
    bool SavedFiltersSupported,
    string SavedFiltersDecision,
    int MaxPageSize,
    int MaxBulkItems);

public sealed record CurationHealth(bool IsReady, string Provider, string Detail);

public sealed class CurationRequestException : Exception
{
    public CurationRequestException(string code, string message, int statusCode, object? current = null) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Current = current;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public object? Current { get; }
}

public interface ICurationService
{
    ValueTask<CurationHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    ValueTask<CurationSearchResult> SearchAsync(CurationSearchRequest request, CancellationToken cancellationToken = default);
    ValueTask<AssetMetadataSnapshot?> GetMetadataAsync(Guid assetId, CancellationToken cancellationToken = default);
    ValueTask<AssetMetadataSnapshot> UpdateMetadataAsync(Guid assetId, AssetMetadataUpdateRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<BulkMetadataResult> BulkUpdateMetadataAsync(BulkMetadataRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CollectionSnapshot>> ListCollectionsAsync(CancellationToken cancellationToken = default);
    ValueTask<CollectionSnapshot> CreateCollectionAsync(CreateCollectionRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<CollectionSnapshot> AddToCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default);
    ValueTask<CollectionSnapshot> RemoveFromCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default);
    ValueTask<AssetMetadataSnapshot> SetArchivedAsync(Guid assetId, bool archived, long expectedVersion, string actorId, CancellationToken cancellationToken = default);
    CurationPolicy Policy { get; }
}

public static class CurationTextNormalizer
{
    public static string NormalizeSearch(string? value)
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
                _ => character
            });
        }
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null) return Array.Empty<string>();
        return tags
            .Select(tag => tag?.Trim() ?? string.Empty)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }
}
