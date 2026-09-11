namespace MAM.Application.Storage;

public interface IStorageObjectStore
{
    string TargetId { get; }
    Task<StorageWriteResult> WriteAsync(string objectKey, Stream source, string expectedSha256, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task<StorageVerificationResult> VerifyAsync(string objectKey, string expectedSha256, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
    Task<StorageTargetHealth> GetHealthAsync(CancellationToken cancellationToken);
}

public sealed record StorageWriteResult(string ObjectKey, long Length, string Sha256);
public sealed record StorageVerificationResult(bool Exists, bool ChecksumMatches, long? Length, string? ActualSha256);
public sealed record StorageTargetHealth(bool IsReady, string TargetId, string Provider, string Detail);
