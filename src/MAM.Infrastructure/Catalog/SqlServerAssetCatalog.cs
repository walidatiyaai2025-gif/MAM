using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Domain.Assets;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Catalog;

public sealed class SqlServerAssetCatalog : IAssetCatalog
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;

    public SqlServerAssetCatalog(SqlServerConnectionFactory connections, IAuditSink audit)
    {
        _connections = connections;
        _audit = audit;
    }

    public async ValueTask<IReadOnlyList<AssetSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT AssetId, Title, Lifecycle, Version, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.MediaAsset
            ORDER BY UpdatedAtUtc DESC, AssetId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var assets = new List<AssetSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) assets.Add(ReadSnapshot(reader));
        return assets;
    }

    public async ValueTask<AssetSnapshot?> GetAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await GetAsync(connection, assetId.Value, cancellationToken);
    }

    public async ValueTask<CatalogMutationResult> CreateAsync(string title, string actorId, CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = NormalizeTitle(title); }
        catch (ArgumentException ex) { return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: ex.Message); }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            INSERT dbo.MediaAsset(AssetId, Title, Lifecycle, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES(@Id, @Title, 0, 1, @CreatedAtUtc, @UpdatedAtUtc);
            """;
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Title", normalized);
            command.Parameters.AddWithValue("@CreatedAtUtc", now.UtcDateTime);
            command.Parameters.AddWithValue("@UpdatedAtUtc", now.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var snapshot = new AssetSnapshot(id, normalized, AssetLifecycleState.Draft.ToString(), 1, now, now);
        await _audit.AppendAsync(NewAudit(actorId, "catalog.asset.created", id, "Success", "version=1"), cancellationToken);
        return new CatalogMutationResult(CatalogMutationStatus.Created, snapshot);
    }

    public async ValueTask<CatalogMutationResult> UpdateTitleAsync(
        AssetId assetId,
        string title,
        long expectedVersion,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = NormalizeTitle(title); }
        catch (ArgumentException ex) { return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: ex.Message); }

        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MediaAsset
            SET Title = @Title,
                Version = Version + 1,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE AssetId = @Id AND Version = @ExpectedVersion;
            """;
        int affected;
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@Id", assetId.Value);
            command.Parameters.AddWithValue("@Title", normalized);
            command.Parameters.AddWithValue("@ExpectedVersion", expectedVersion);
            command.Parameters.AddWithValue("@UpdatedAtUtc", now.UtcDateTime);
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var current = await GetAsync(connection, assetId.Value, cancellationToken);
        if (affected == 0)
        {
            if (current is null) return new CatalogMutationResult(CatalogMutationStatus.NotFound);
            await _audit.AppendAsync(NewAudit(actorId, "catalog.asset.title-update-conflict", assetId.Value, "Conflict", $"expected={expectedVersion};current={current.Version}"), cancellationToken);
            return new CatalogMutationResult(
                CatalogMutationStatus.Conflict,
                current,
                "The asset was changed by another request. Refresh and retry with the current version.");
        }

        if (current is null)
            return new CatalogMutationResult(CatalogMutationStatus.Unavailable, Error: "Updated asset could not be re-read from SQL Server.");

        await _audit.AppendAsync(NewAudit(actorId, "catalog.asset.title-updated", assetId.Value, "Success", $"version={current.Version}"), cancellationToken);
        return new CatalogMutationResult(CatalogMutationStatus.Updated, current);
    }

    public async ValueTask<CatalogHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId = N'0001_p02_core_catalog';";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return value == 1
                ? new CatalogHealth(true, "SqlServer", "Authoritative SQL Server catalog and P02 core schema are reachable.")
                : new CatalogHealth(false, "SqlServer", "SQL Server is reachable but the P02 core migration is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new CatalogHealth(false, "SqlServer", $"Authoritative SQL Server catalog is unavailable: {ex.GetType().Name}.");
        }
    }

    private async ValueTask<AssetSnapshot?> GetAsync(SqlConnection connection, Guid assetId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT AssetId, Title, Lifecycle, Version, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.MediaAsset
            WHERE AssetId = @Id;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Id", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSnapshot(reader) : null;
    }

    private static AssetSnapshot ReadSnapshot(SqlDataReader reader)
    {
        var lifecycleValue = reader.GetByte(2);
        var lifecycle = Enum.IsDefined(typeof(AssetLifecycleState), (int)lifecycleValue)
            ? ((AssetLifecycleState)lifecycleValue).ToString()
            : $"Unknown({lifecycleValue})";
        return new AssetSnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            lifecycle,
            reader.GetInt64(3),
            Utc(reader.GetDateTime(4)),
            Utc(reader.GetDateTime(5)));
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string NormalizeTitle(string title)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new ArgumentException("Asset title is required.", nameof(title));
        if (normalized.Length > 300) throw new ArgumentException("Asset title cannot exceed 300 characters.", nameof(title));
        return normalized;
    }

    private static AuditEvent NewAudit(string actorId, string action, Guid entityId, string outcome, string? detail) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, actorId, action, "MediaAsset", entityId.ToString("D"), outcome, detail);
}
