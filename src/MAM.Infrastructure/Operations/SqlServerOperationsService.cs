using MAM.Application.Operations;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Operations;

public sealed class SqlServerOperationsService : IOperationsService
{
    private readonly SqlServerConnectionFactory _connections;

    public SqlServerOperationsService(SqlServerConnectionFactory connections) => _connections = connections;

    public async ValueTask<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            await command.ExecuteScalarAsync(cancellationToken);
            return new OperationsHealth(true, "SqlServer", "Authoritative operational reporting store is reachable.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new OperationsHealth(false, "SqlServer", "Authoritative operational reporting store is unavailable.");
        }
    }

    public async Task<OperationalSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.MediaAsset),
 (SELECT COUNT_BIG(*) FROM dbo.MamMediaOriginal),
 (SELECT COALESCE(SUM([Length]),0) FROM dbo.MamMediaOriginal),
 (SELECT COUNT_BIG(*) FROM dbo.MamUploadSession WHERE State=0),
 (SELECT COUNT_BIG(*) FROM dbo.MamUploadSession WHERE State IN (2,3,5)),
 (SELECT COUNT_BIG(*) FROM dbo.MamProcessingJob WHERE State=0),
 (SELECT COUNT_BIG(*) FROM dbo.MamProcessingJob WHERE State=1),
 (SELECT COUNT_BIG(*) FROM dbo.MamProcessingJob WHERE State=3),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupJob WHERE State=0),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupJob WHERE State=1),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupJob WHERE State=3),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=1),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=0),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=2),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=3)
""";
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
        return new OperationalSummary(
            reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt64(reader.GetValue(2)),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7),
            reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12),
            reader.GetInt64(13), reader.GetInt64(14), DateTimeOffset.UtcNow);
    }

    public async Task<IngestThroughputReport> GetIngestThroughputAsync(int windowHours, CancellationToken cancellationToken = default)
    {
        if (windowHours is < 1 or > 24 * 31)
            throw new OperationsRequestException("invalid_window", "windowHours must be between 1 and 744.");

        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.AddHours(-windowHours);
        const string sql = """
SELECT COUNT_BIG(*), COALESCE(SUM(ExpectedLength),0)
FROM dbo.MamUploadSession
WHERE State=1 AND UpdatedAtUtc >= @fromUtc AND UpdatedAtUtc <= @toUtc
""";
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@fromUtc", fromUtc.UtcDateTime);
        command.Parameters.AddWithValue("@toUtc", toUtc.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
        var sessions = reader.GetInt64(0);
        var bytes = Convert.ToInt64(reader.GetValue(1));
        var average = bytes / Math.Max(1d, (toUtc - fromUtc).TotalSeconds);
        return new IngestThroughputReport(windowHours, fromUtc, toUtc, sessions, bytes, average);
    }

    public async Task<DurableQueueReport> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT N'Processing',
 SUM(CASE WHEN State=0 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=1 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=3 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=1 AND LeaseExpiresAtUtc < SYSUTCDATETIME() THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 MIN(CASE WHEN State=0 THEN CreatedAtUtc END)
FROM dbo.MamProcessingJob
UNION ALL
SELECT N'Backup',
 SUM(CASE WHEN State=0 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=1 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=3 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 SUM(CASE WHEN State=1 AND LeaseExpiresAtUtc < SYSUTCDATETIME() THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 MIN(CASE WHEN State=0 THEN CreatedAtUtc END)
FROM dbo.MamBackupJob
UNION ALL
SELECT N'CaptureUploadHandoff',
 SUM(CASE WHEN State=0 THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 CAST(0 AS bigint),
 SUM(CASE WHEN State IN (2,3,5) THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),
 CAST(0 AS bigint),
 MIN(CASE WHEN State=0 THEN CreatedAtUtc END)
FROM dbo.MamUploadSession
""";
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DurableQueueState>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new DurableQueueState(
                reader.GetString(0), ToInt64(reader, 1), ToInt64(reader, 2), ToInt64(reader, 3), ToInt64(reader, 4),
                reader.IsDBNull(5) ? null : Utc(reader.GetDateTime(5))));
        }
        return new DurableQueueReport(items, DateTimeOffset.UtcNow);
    }

    public async Task<IntegrityProtectionReport> GetIntegrityAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.MamMediaOriginal),
 (SELECT COALESCE(SUM([Length]),0) FROM dbo.MamMediaOriginal),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=1),
 (SELECT COALESCE(SUM(ExpectedLength),0) FROM dbo.MamBackupProtection WHERE State=1),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=0),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=2),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=3),
 (SELECT MIN(LastIntegrityCheckAtUtc) FROM dbo.MamBackupProtection WHERE State=1)
""";
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
        return new IntegrityProtectionReport(
            reader.GetInt64(0), Convert.ToInt64(reader.GetValue(1)), reader.GetInt64(2), Convert.ToInt64(reader.GetValue(3)),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6),
            reader.IsDBNull(7) ? null : Utc(reader.GetDateTime(7)), DateTimeOffset.UtcNow);
    }

    public async Task<StorageUsageReport> GetStorageUsageAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
 COALESCE((SELECT TOP (1) StorageTargetId FROM dbo.MamMediaOriginal ORDER BY CreatedAtUtc DESC), N'NotObserved'),
 COALESCE((SELECT TOP (1) BackupTargetId FROM dbo.MamBackupProtection ORDER BY UpdatedAtUtc DESC), N'NotObserved'),
 (SELECT COALESCE(SUM([Length]),0) FROM dbo.MamMediaOriginal),
 (SELECT COALESCE(SUM(ExpectedLength),0) FROM dbo.MamBackupProtection WHERE State=1),
 (SELECT COUNT_BIG(*) FROM dbo.MamMediaOriginal),
 (SELECT COUNT_BIG(*) FROM dbo.MamBackupProtection WHERE State=1)
""";
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
        return new StorageUsageReport(reader.GetString(0), reader.GetString(1),
            Convert.ToInt64(reader.GetValue(2)), Convert.ToInt64(reader.GetValue(3)), reader.GetInt64(4), reader.GetInt64(5), DateTimeOffset.UtcNow);
    }

    private static long ToInt64(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static OperationsRequestException Unavailable() => new("operations_unavailable", "Authoritative operations query returned no result.", 503);
}
