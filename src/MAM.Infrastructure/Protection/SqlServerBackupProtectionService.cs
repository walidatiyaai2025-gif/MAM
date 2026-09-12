using MAM.Application.Auditing;
using MAM.Application.Protection;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Protection;

public sealed class SqlServerBackupProtectionService : IBackupProtectionService
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IStorageObjectStore _backup;
    private readonly IAuditSink _audit;
    private readonly MamSettings _settings;

    public SqlServerBackupProtectionService(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IAuditSink audit,
        MamSettings settings)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _settings = settings;
        var backup = settings.Storage.Backup;
        _backup = new FileSystemStorageObjectStore(new PrimaryStorageTargetSettings
        {
            Id = backup.Id,
            Type = backup.Type,
            Root = backup.Root,
            CredentialRef = backup.CredentialRef,
            MinimumFreeGB = backup.MinimumFreeGB,
            MinimumFreePercent = 0,
            WriteTestOnHealthCheck = true,
            OriginalsPrefix = "originals",
            DerivativesPrefix = "derivatives",
            PathLayout = "server-generated"
        });
        if (string.Equals(_primary.TargetId, _backup.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup Storage target must be distinct from Primary Storage target.");
    }

    public async Task<BackupProtectionHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var primary = await _primary.GetHealthAsync(cancellationToken);
        var backup = await _backup.GetHealthAsync(cancellationToken);
        if (!primary.IsReady) return new(false, _primary.TargetId, _backup.TargetId, "Primary Storage is degraded; protection cannot advance.");
        if (!backup.IsReady) return new(false, _primary.TargetId, _backup.TargetId, "Backup Storage is degraded; assets remain non-Protected.");
        return new(true, _primary.TargetId, _backup.TargetId, "Primary and independent Backup Storage targets are ready.");
    }

    public async Task<int> QueueEligibleOriginalsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT dbo.MamBackupProtection(AssetId, PrimaryTargetId, PrimaryObjectKey, BackupTargetId, BackupObjectKey, ExpectedLength, ExpectedSha256, State, UpdatedAtUtc)
SELECT o.AssetId, o.StorageTargetId, o.ObjectKey, @backupTarget,
       CONCAT(N'originals/', CONVERT(nvarchar(36), o.AssetId), N'/', LOWER(o.Sha256)),
       o.Length, LOWER(o.Sha256), 0, SYSUTCDATETIME()
FROM dbo.MamMediaOriginal o WITH (UPDLOCK, HOLDLOCK)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MamBackupProtection p WHERE p.AssetId=o.AssetId);
DECLARE @inserted int = @@ROWCOUNT;
INSERT dbo.MamBackupJob(JobId, AssetId, State, AvailableAtUtc, CreatedAtUtc, UpdatedAtUtc)
SELECT NEWID(), p.AssetId, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
FROM dbo.MamBackupProtection p
WHERE p.State IN (0,2,3)
  AND NOT EXISTS (SELECT 1 FROM dbo.MamBackupJob j WHERE j.AssetId=p.AssetId AND j.State IN (0,1));
COMMIT TRANSACTION;
SELECT @inserted;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@backupTarget", _backup.TargetId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<BackupJobLease?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("Worker ID is required.", nameof(workerId));
        var leaseSeconds = Math.Max(15, _settings.Jobs.LeaseSeconds);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @job uniqueidentifier;
SELECT TOP (1) @job=j.JobId
FROM dbo.MamBackupJob j WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE (j.State=0 AND j.AvailableAtUtc<=SYSUTCDATETIME())
   OR (j.State=1 AND j.LeaseExpiresAtUtc<SYSUTCDATETIME())
ORDER BY j.AvailableAtUtc, j.CreatedAtUtc;
IF @job IS NOT NULL
BEGIN
  UPDATE dbo.MamBackupJob
  SET State=1, LeaseOwner=@worker, LeaseExpiresAtUtc=DATEADD(second,@leaseSeconds,SYSUTCDATETIME()),
      LastHeartbeatAtUtc=SYSUTCDATETIME(), AttemptCount=AttemptCount+1, UpdatedAtUtc=SYSUTCDATETIME()
  WHERE JobId=@job;
END;
SELECT j.JobId,j.AssetId,p.PrimaryObjectKey,p.BackupObjectKey,p.ExpectedLength,p.ExpectedSha256,j.AttemptCount,j.LeaseExpiresAtUtc
FROM dbo.MamBackupJob j JOIN dbo.MamBackupProtection p ON p.AssetId=j.AssetId
WHERE j.JobId=@job;
COMMIT TRANSACTION;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@worker", workerId.Trim());
        command.Parameters.AddWithValue("@leaseSeconds", leaseSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new BackupJobLease(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5).ToLowerInvariant(), reader.GetInt32(6), new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero));
    }

    public async Task<BackupProtectionRecord> ExecuteAsync(BackupJobLease lease, string workerId, CancellationToken cancellationToken = default)
    {
        var primaryBefore = await _primary.VerifyAsync(lease.PrimaryObjectKey, lease.ExpectedSha256, cancellationToken);
        if (!primaryBefore.Exists || !primaryBefore.ChecksumMatches || primaryBefore.Length != lease.ExpectedLength)
            return await FailAsync(lease, workerId, BackupProtectionState.Mismatch, "Authoritative Primary original failed pre-copy verification.", cancellationToken);

        try
        {
            var existing = await _backup.VerifyAsync(lease.BackupObjectKey, lease.ExpectedSha256, cancellationToken);
            if (!existing.Exists)
            {
                await using var source = await _primary.OpenReadAsync(lease.PrimaryObjectKey, cancellationToken);
                var written = await _backup.WriteAsync(lease.BackupObjectKey, source, lease.ExpectedSha256, cancellationToken);
                if (written.Length != lease.ExpectedLength)
                    return await FailAsync(lease, workerId, BackupProtectionState.Mismatch, "Backup write length differs from authoritative Primary length.", cancellationToken);
            }

            var verified = await _backup.VerifyAsync(lease.BackupObjectKey, lease.ExpectedSha256, cancellationToken);
            if (!verified.Exists || !verified.ChecksumMatches || verified.Length != lease.ExpectedLength)
                return await FailAsync(lease, workerId, BackupProtectionState.Mismatch, "Backup copy exists but SHA-256/length parity verification failed.", cancellationToken);

            var primaryAfter = await _primary.VerifyAsync(lease.PrimaryObjectKey, lease.ExpectedSha256, cancellationToken);
            if (!primaryAfter.Exists || !primaryAfter.ChecksumMatches || primaryAfter.Length != lease.ExpectedLength)
                return await FailAsync(lease, workerId, BackupProtectionState.Mismatch, "Primary original changed during Backup protection execution.", cancellationToken);

            await CompleteAsync(lease, workerId, cancellationToken);
            await AuditAsync(lease.AssetId, workerId, "backup.protected", "Success", $"backup={_backup.TargetId}; sha256 verified; length={lease.ExpectedLength}", cancellationToken);
            return (await GetAsync(lease.AssetId, cancellationToken))!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var detail = ex.Message.Length <= 900 ? ex.Message : ex.Message[..900];
            return await FailAsync(lease, workerId, BackupProtectionState.BackupFailed, detail, cancellationToken);
        }
    }

    public async Task<BackupProtectionRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"SELECT AssetId,PrimaryTargetId,PrimaryObjectKey,BackupTargetId,BackupObjectKey,ExpectedLength,ExpectedSha256,State,AttemptCount,LastAttemptAtUtc,VerifiedAtUtc,LastIntegrityCheckAtUtc,LastError FROM dbo.MamBackupProtection WHERE AssetId=@assetId", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@assetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProtection(reader) : null;
    }

    public async Task<BackupProtectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"SELECT SUM(CASE WHEN State=0 THEN 1 ELSE 0 END),SUM(CASE WHEN State=1 THEN 1 ELSE 0 END),SUM(CASE WHEN State=2 THEN 1 ELSE 0 END),SUM(CASE WHEN State=3 THEN 1 ELSE 0 END) FROM dbo.MamBackupProtection", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        static long V(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
        return new(V(reader,0),V(reader,1),V(reader,2),V(reader,3),DateTimeOffset.UtcNow);
    }

    public async Task<int> QueueIntegrityRechecksAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"
INSERT dbo.MamBackupJob(JobId,AssetId,State,AvailableAtUtc,CreatedAtUtc,UpdatedAtUtc)
SELECT NEWID(),p.AssetId,0,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME()
FROM dbo.MamBackupProtection p
WHERE p.State=1 AND (p.LastIntegrityCheckAtUtc IS NULL OR p.LastIntegrityCheckAtUtc<@olderThan)
AND NOT EXISTS (SELECT 1 FROM dbo.MamBackupJob j WHERE j.AssetId=p.AssetId AND j.State IN (0,1));
SELECT @@ROWCOUNT;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@olderThan", olderThanUtc.UtcDateTime);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CompleteAsync(BackupJobLease lease, string workerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"
SET XACT_ABORT ON; BEGIN TRANSACTION;
UPDATE dbo.MamBackupProtection SET State=1,AttemptCount=AttemptCount+1,LastAttemptAtUtc=SYSUTCDATETIME(),VerifiedAtUtc=SYSUTCDATETIME(),LastIntegrityCheckAtUtc=SYSUTCDATETIME(),LastError=NULL,UpdatedAtUtc=SYSUTCDATETIME() WHERE AssetId=@assetId;
UPDATE dbo.MamBackupJob SET State=2,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=SYSUTCDATETIME(),LastError=NULL,UpdatedAtUtc=SYSUTCDATETIME() WHERE JobId=@jobId AND LeaseOwner=@worker;
COMMIT TRANSACTION;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@assetId", lease.AssetId);
        command.Parameters.AddWithValue("@jobId", lease.JobId);
        command.Parameters.AddWithValue("@worker", workerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<BackupProtectionRecord> FailAsync(BackupJobLease lease, string workerId, BackupProtectionState state, string error, CancellationToken cancellationToken)
    {
        var retry = lease.AttemptCount < Math.Max(1, _settings.Storage.Backup.MaxRetryCount);
        var backoff = Math.Max(1, _settings.Storage.Backup.RetryBackoffSeconds) * Math.Max(1, lease.AttemptCount);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"
SET XACT_ABORT ON; BEGIN TRANSACTION;
UPDATE dbo.MamBackupProtection SET State=@state,AttemptCount=AttemptCount+1,LastAttemptAtUtc=SYSUTCDATETIME(),VerifiedAtUtc=NULL,LastError=@error,UpdatedAtUtc=SYSUTCDATETIME() WHERE AssetId=@assetId;
UPDATE dbo.MamBackupJob SET State=CASE WHEN @retry=1 THEN 0 ELSE 3 END,AvailableAtUtc=CASE WHEN @retry=1 THEN DATEADD(second,@backoff,SYSUTCDATETIME()) ELSE AvailableAtUtc END,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastError=@error,UpdatedAtUtc=SYSUTCDATETIME() WHERE JobId=@jobId AND LeaseOwner=@worker;
COMMIT TRANSACTION;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@state", (byte)state);
        command.Parameters.AddWithValue("@error", error);
        command.Parameters.AddWithValue("@assetId", lease.AssetId);
        command.Parameters.AddWithValue("@jobId", lease.JobId);
        command.Parameters.AddWithValue("@worker", workerId);
        command.Parameters.AddWithValue("@retry", retry);
        command.Parameters.AddWithValue("@backoff", backoff);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(lease.AssetId, workerId, state==BackupProtectionState.Mismatch ? "backup.mismatch" : "backup.failed", "Failure", error, cancellationToken);
        return (await GetAsync(lease.AssetId, cancellationToken))!;
    }

    private async Task AuditAsync(Guid assetId, string actor, string action, string outcome, string detail, CancellationToken cancellationToken) =>
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, "MediaAsset", assetId.ToString("D"), outcome, detail), cancellationToken);

    private static BackupProtectionRecord ReadProtection(SqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetString(6).ToLowerInvariant(),
        (BackupProtectionState)r.GetByte(7), r.GetInt32(8),
        r.IsDBNull(9)?null:new DateTimeOffset(r.GetDateTime(9),TimeSpan.Zero),
        r.IsDBNull(10)?null:new DateTimeOffset(r.GetDateTime(10),TimeSpan.Zero),
        r.IsDBNull(11)?null:new DateTimeOffset(r.GetDateTime(11),TimeSpan.Zero),
        r.IsDBNull(12)?null:r.GetString(12));
}
