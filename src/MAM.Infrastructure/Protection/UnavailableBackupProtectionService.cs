using MAM.Application.Protection;

namespace MAM.Infrastructure.Protection;

public sealed class UnavailableBackupProtectionService : IBackupProtectionService
{
    private readonly string _detail;
    private readonly string _primaryTargetId;
    private readonly string _backupTargetId;

    public UnavailableBackupProtectionService(string detail, string primaryTargetId, string backupTargetId)
    {
        _detail = detail;
        _primaryTargetId = primaryTargetId;
        _backupTargetId = backupTargetId;
    }

    public Task<BackupProtectionHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BackupProtectionHealth(false, _primaryTargetId, _backupTargetId, _detail));

    public Task<int> QueueEligibleOriginalsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<BackupJobLease?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default) => Task.FromResult<BackupJobLease?>(null);
    public Task<BackupProtectionRecord> ExecuteAsync(BackupJobLease lease, string workerId, CancellationToken cancellationToken = default) =>
        Task.FromException<BackupProtectionRecord>(new InvalidOperationException(_detail));
    public Task<BackupProtectionRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult<BackupProtectionRecord?>(null);
    public Task<BackupProtectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BackupProtectionSummary(0, 0, 0, 0, DateTimeOffset.UtcNow));
    public Task<int> QueueIntegrityRechecksAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
