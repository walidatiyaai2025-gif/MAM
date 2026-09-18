using MAM.Application.BulkImport;

namespace MAM.Infrastructure.BulkImport;

public sealed record BulkImportSessionRow(
    Guid SessionId,
    string RootFolderName,
    BulkImportSessionState State,
    int TotalFiles,
    long TotalBytes,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record BulkImportItemRow(
    Guid ItemId,
    Guid SessionId,
    string RelativePath,
    string FileName,
    string CategoryName,
    Guid? CategoryId,
    long ExpectedLength,
    string? ExpectedSha256,
    Guid? UploadSessionId,
    Guid? AssetId,
    BulkImportItemState State,
    string? ReasonCode,
    string? Detail,
    DateTimeOffset UpdatedAtUtc);

public interface IBulkImportStateStore
{
    Task CreateAsync(BulkImportSessionRow session, IReadOnlyList<BulkImportItemRow> items, CancellationToken cancellationToken = default);
    Task<(BulkImportSessionRow Session, IReadOnlyList<BulkImportItemRow> Items)?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BulkImportSessionRow>> ListRecentAsync(string createdBy, int limit, CancellationToken cancellationToken = default);
    Task UpdateSessionAsync(BulkImportSessionRow session, CancellationToken cancellationToken = default);
    Task UpdateItemAsync(BulkImportItemRow item, CancellationToken cancellationToken = default);
}
