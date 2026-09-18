namespace MAM.Application.BulkImport;

public enum BulkImportSessionState
{
    Preparing = 0,
    Ready = 1,
    Running = 2,
    Completed = 3,
    CompletedWithErrors = 4,
    Cancelled = 5
}

public enum BulkImportItemState
{
    Pending = 0,
    Uploading = 1,
    Uploaded = 2,
    AlreadyExists = 3,
    Linked = 4,
    Failed = 5,
    Unsupported = 6,
    Cancelled = 7
}

public sealed record BulkImportFileDescriptor(
    string RelativePath,
    long Length,
    string? Sha256);

public sealed record CreateBulkImportSessionRequest(
    string RootFolderName,
    IReadOnlyList<BulkImportFileDescriptor> Files);

public sealed record BulkImportItemSnapshot(
    Guid ItemId,
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
    long ReceivedLength);

public sealed record BulkImportSessionSummary(
    Guid SessionId,
    string RootFolderName,
    BulkImportSessionState State,
    int TotalFiles,
    int ProcessedFiles,
    int Uploaded,
    int AlreadyExists,
    int Linked,
    int Failed,
    int Unsupported,
    long TotalBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record BulkImportSessionSnapshot(
    Guid SessionId,
    string RootFolderName,
    BulkImportSessionState State,
    int TotalFiles,
    int ProcessedFiles,
    int Uploaded,
    int AlreadyExists,
    int Linked,
    int Failed,
    int Unsupported,
    int Cancelled,
    long TotalBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<BulkImportItemSnapshot> Items);

public sealed record BulkImportFailureRequest(string Code, string? Detail);

public sealed record BulkImportReport(
    string FileName,
    string ContentType,
    string Content);

public sealed class BulkImportRequestException : Exception
{
    public BulkImportRequestException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public interface IBulkImportService
{
    Task<BulkImportSessionSnapshot> CreateSessionAsync(CreateBulkImportSessionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportSessionSnapshot> GetSessionAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BulkImportSessionSummary>> ListRecentAsync(string actorId, int limit = 20, CancellationToken cancellationToken = default);
    Task<BulkImportItemSnapshot> BeginItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportItemSnapshot> FinalizeItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportItemSnapshot> FailItemAsync(Guid sessionId, Guid itemId, BulkImportFailureRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportItemSnapshot> RetryItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportSessionSnapshot> CancelAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default);
    Task<BulkImportReport> GetReportAsync(Guid sessionId, string format, string actorId, CancellationToken cancellationToken = default);
}
