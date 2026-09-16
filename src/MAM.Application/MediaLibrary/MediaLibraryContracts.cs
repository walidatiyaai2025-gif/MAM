using MAM.Application.Discovery;

namespace MAM.Application.MediaLibrary;

public sealed record MediaLibraryAssetSnapshot(
    Guid AssetId,
    string Title,
    string Lifecycle,
    long Version,
    DateTimeOffset UploadedAtUtc,
    DateOnly? ProductionDate,
    string MediaKind,
    Guid CategoryId,
    Guid? ParentCategoryId,
    string CategoryNameEn,
    string? CategoryNameAr,
    DateTimeOffset UpdatedAtUtc);

public sealed record MediaLibrarySnapshot(
    IReadOnlyList<CategorySnapshot> Categories,
    IReadOnlyList<MediaLibraryAssetSnapshot> Assets);

public sealed record UpdateMediaOrganizationRequest(
    long ExpectedVersion,
    DateOnly? ProductionDate,
    Guid? CategoryId);

public sealed class MediaLibraryRequestException : Exception
{
    public MediaLibraryRequestException(string code, string message, int statusCode = 400, MediaLibraryAssetSnapshot? current = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Current = current;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public MediaLibraryAssetSnapshot? Current { get; }
}
