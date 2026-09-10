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
