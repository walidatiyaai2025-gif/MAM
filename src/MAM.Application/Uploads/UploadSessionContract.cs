namespace MAM.Application.Uploads;

public sealed record UploadSessionContract(
    Guid SessionId,
    Guid AssetId,
    string OriginalFileName,
    long ExpectedLength,
    string ExpectedSha256,
    int ChunkSizeBytes,
    DateTimeOffset ExpiresAtUtc);

public sealed record UploadChunkReceipt(long Offset, int Length, string ChunkSha256);

public enum UploadSessionState
{
    Receiving,
    Completed,
    Failed,
    Quarantined,
    Duplicate,
    Expired
}

public sealed record CreateUploadSessionRequest(
    string Title,
    string OriginalFileName,
    long ExpectedLength,
    string ExpectedSha256);

public sealed record UploadSessionSnapshot(
    UploadSessionContract Session,
    string Title,
    long ReceivedLength,
    UploadSessionState State,
    bool IsQuarantined,
    string? PrimaryObjectKey,
    string? Error);

public sealed record UploadChunkResult(
    Guid SessionId,
    long ReceivedLength,
    long ExpectedLength,
    bool ReadyToFinalize,
    UploadChunkReceipt Receipt);

public sealed record UploadFinalizeResult(
    Guid SessionId,
    Guid AssetId,
    string PrimaryObjectKey,
    long Length,
    string Sha256,
    UploadSessionState State);

public sealed record UploadHealth(
    bool IsReady,
    string Provider,
    string TargetId,
    string Detail);

public sealed class UploadRequestException : Exception
{
    public UploadRequestException(string code, string message, int statusCode = 400, Guid? existingAssetId = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        ExistingAssetId = existingAssetId;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public Guid? ExistingAssetId { get; }
}

public interface IDurableUploadService
{
    ValueTask<UploadSessionSnapshot> CreateSessionAsync(CreateUploadSessionRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<UploadSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    ValueTask<UploadChunkResult> PutChunkAsync(Guid sessionId, long offset, string expectedChunkSha256, Stream body, string actorId, CancellationToken cancellationToken = default);
    ValueTask<UploadFinalizeResult> FinalizeAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default);
    ValueTask<UploadHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
