namespace MAM.Application.Operations;

public sealed record OperationsHealth(bool IsReady, string Provider, string Detail);

public sealed record OperationalSummary(
    long Assets,
    long Originals,
    long OriginalBytes,
    long UploadReceiving,
    long UploadFailed,
    long ProcessingQueued,
    long ProcessingLeased,
    long ProcessingFailed,
    long BackupQueued,
    long BackupLeased,
    long BackupFailed,
    long Protected,
    long ProtectionPending,
    long ProtectionFailed,
    long ProtectionMismatch,
    DateTimeOffset GeneratedAtUtc);

public sealed record IngestThroughputReport(
    int WindowHours,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long CompletedSessions,
    long CompletedBytes,
    double AverageBytesPerSecond);

public sealed record DurableQueueState(
    string Queue,
    long Pending,
    long Leased,
    long Failed,
    long StaleLeases,
    DateTimeOffset? OldestPendingAtUtc);

public sealed record DurableQueueReport(
    IReadOnlyList<DurableQueueState> Queues,
    DateTimeOffset GeneratedAtUtc);

public sealed record IntegrityProtectionReport(
    long Originals,
    long OriginalBytes,
    long Protected,
    long ProtectedBytes,
    long Pending,
    long Failed,
    long Mismatch,
    DateTimeOffset? OldestIntegrityCheckAtUtc,
    DateTimeOffset GeneratedAtUtc);

public sealed record StorageUsageReport(
    string PrimaryTargetId,
    string BackupTargetId,
    long AuthoritativeOriginalBytes,
    long VerifiedProtectedBytes,
    long OriginalCount,
    long ProtectedCount,
    DateTimeOffset GeneratedAtUtc);

public sealed record DependencyHealthItem(string Dependency, string Status, string Detail, string? TargetId = null);

public sealed record DependencyHealthReport(IReadOnlyList<DependencyHealthItem> Items, DateTimeOffset GeneratedAtUtc);

public sealed record DiagnosticsBundle(
    string Product,
    string Phase,
    string Environment,
    string SiteCode,
    string Version,
    string CommitSha,
    string CorrelationId,
    OperationalSummary Summary,
    DurableQueueReport Queues,
    IntegrityProtectionReport Integrity,
    DependencyHealthReport Dependencies,
    IReadOnlyList<string> RedactionPolicy,
    DateTimeOffset GeneratedAtUtc);

public sealed class OperationsRequestException : Exception
{
    public OperationsRequestException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public interface IOperationsService
{
    ValueTask<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<OperationalSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IngestThroughputReport> GetIngestThroughputAsync(int windowHours, CancellationToken cancellationToken = default);
    Task<DurableQueueReport> GetQueuesAsync(CancellationToken cancellationToken = default);
    Task<IntegrityProtectionReport> GetIntegrityAsync(CancellationToken cancellationToken = default);
    Task<StorageUsageReport> GetStorageUsageAsync(CancellationToken cancellationToken = default);
}

public sealed class UnavailableOperationsService : IOperationsService
{
    private readonly string _reason;
    public UnavailableOperationsService(string reason) => _reason = reason;
    public ValueTask<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new OperationsHealth(false, "Unavailable", _reason));
    private OperationsRequestException Failure() => new("operations_unavailable", _reason, 503);
    public Task<OperationalSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => throw Failure();
    public Task<IngestThroughputReport> GetIngestThroughputAsync(int windowHours, CancellationToken cancellationToken = default) => throw Failure();
    public Task<DurableQueueReport> GetQueuesAsync(CancellationToken cancellationToken = default) => throw Failure();
    public Task<IntegrityProtectionReport> GetIntegrityAsync(CancellationToken cancellationToken = default) => throw Failure();
    public Task<StorageUsageReport> GetStorageUsageAsync(CancellationToken cancellationToken = default) => throw Failure();
}
