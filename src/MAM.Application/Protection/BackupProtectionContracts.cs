namespace MAM.Application.Protection;

public enum BackupProtectionState : byte
{
    BackupPending = 0,
    Protected = 1,
    BackupFailed = 2,
    Mismatch = 3
}

public sealed record BackupProtectionRecord(
    Guid AssetId,
    string PrimaryTargetId,
    string PrimaryObjectKey,
    string BackupTargetId,
    string BackupObjectKey,
    long ExpectedLength,
    string ExpectedSha256,
    BackupProtectionState State,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? LastIntegrityCheckAtUtc,
    string? LastError);

public sealed record BackupJobLease(
    Guid JobId,
    Guid AssetId,
    string PrimaryObjectKey,
    string BackupObjectKey,
    long ExpectedLength,
    string ExpectedSha256,
    int AttemptCount,
    DateTimeOffset LeaseExpiresAtUtc);

public sealed record BackupProtectionSummary(
    long Pending,
    long Protected,
    long Failed,
    long Mismatch,
    DateTimeOffset GeneratedAtUtc);

public sealed record BackupProtectionHealth(
    bool IsReady,
    string PrimaryTargetId,
    string BackupTargetId,
    string Detail);

public interface IBackupProtectionService
{
    Task<BackupProtectionHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<int> QueueEligibleOriginalsAsync(CancellationToken cancellationToken = default);
    Task<BackupJobLease?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default);
    Task<BackupProtectionRecord> ExecuteAsync(BackupJobLease lease, string workerId, CancellationToken cancellationToken = default);
    Task<BackupProtectionRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<BackupProtectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<int> QueueIntegrityRechecksAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default);
}
