using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Domain.Assets;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Demo;

public sealed class DemoSqliteAuditSink(DemoSqliteDatabase database) : IAuditSink
{
    public async ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DemoAudit(AuditEventId,OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail)
            VALUES($id,$at,$actor,$action,$type,$entity,$outcome,$detail);
            """;
        command.Parameters.AddWithValue("$id", auditEvent.Id.ToString("D"));
        command.Parameters.AddWithValue("$at", DemoSqliteDatabase.ToDb(auditEvent.OccurredAtUtc));
        command.Parameters.AddWithValue("$actor", auditEvent.ActorId);
        command.Parameters.AddWithValue("$action", auditEvent.Action);
        command.Parameters.AddWithValue("$type", auditEvent.EntityType);
        command.Parameters.AddWithValue("$entity", auditEvent.EntityId);
        command.Parameters.AddWithValue("$outcome", auditEvent.Outcome);
        command.Parameters.AddWithValue("$detail", (object?)auditEvent.Detail ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 5000);
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AuditEventId,OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail
            FROM DemoAudit ORDER BY OccurredAtUtc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new AuditEvent(Guid.Parse(reader.GetString(0)), DemoSqliteDatabase.FromDb(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return result;
    }
}

public sealed class DemoSqliteAssetCatalog(DemoSqliteDatabase database, IAuditSink audit) : IAssetCatalog
{
    public async ValueTask<IReadOnlyList<AssetSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AssetId,Title,Lifecycle,Version,CreatedAtUtc,UpdatedAtUtc FROM DemoAsset ORDER BY UpdatedAtUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<AssetSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(Read(reader));
        return rows;
    }

    public async ValueTask<AssetSnapshot?> GetAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AssetId,Title,Lifecycle,Version,CreatedAtUtc,UpdatedAtUtc FROM DemoAsset WHERE AssetId=$id;";
        command.Parameters.AddWithValue("$id", assetId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public ValueTask<CatalogMutationResult> CreateAsync(string title, string actorId, CancellationToken cancellationToken = default) =>
        CreateWithIdAsync(AssetId.New(), title, actorId, cancellationToken);

    public async ValueTask<CatalogMutationResult> CreateWithIdAsync(AssetId assetId, string title, string actorId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTitle(title, out var error);
        if (error is not null) return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: error);
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO DemoAsset(AssetId,Title,Lifecycle,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES($id,$title,'Draft',1,$now,$now);
                """;
            command.Parameters.AddWithValue("$id", assetId.Value.ToString("D"));
            command.Parameters.AddWithValue("$title", normalized);
            command.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return new CatalogMutationResult(CatalogMutationStatus.Conflict, Error: "The requested asset identity already exists.");
        }
        var snapshot = new AssetSnapshot(assetId.Value, normalized, "Draft", 1, now, now);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), now, actorId, "catalog.asset.created", "MediaAsset", assetId.ToString(), "Success", "provider=Sqlite;version=1"), cancellationToken);
        return new CatalogMutationResult(CatalogMutationStatus.Created, snapshot);
    }

    public async ValueTask<CatalogMutationResult> UpdateTitleAsync(AssetId assetId, string title, long expectedVersion, string actorId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTitle(title, out var error);
        if (error is not null) return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: error);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DemoAsset SET Title=$title,Version=Version+1,UpdatedAtUtc=$now
            WHERE AssetId=$id AND Version=$version;
            """;
        command.Parameters.AddWithValue("$title", normalized);
        command.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(now));
        command.Parameters.AddWithValue("$id", assetId.Value.ToString("D"));
        command.Parameters.AddWithValue("$version", expectedVersion);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
        {
            var current = await GetAsync(assetId, cancellationToken);
            return current is null
                ? new CatalogMutationResult(CatalogMutationStatus.NotFound)
                : new CatalogMutationResult(CatalogMutationStatus.Conflict, current, "The asset was changed by another request. Refresh and retry with the current version.");
        }
        var updated = await GetAsync(assetId, cancellationToken);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), now, actorId, "catalog.asset.title-updated", "MediaAsset", assetId.ToString(), "Success", $"provider=Sqlite;version={updated!.Version}"), cancellationToken);
        return new CatalogMutationResult(CatalogMutationStatus.Updated, updated);
    }

    public async ValueTask<CatalogHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await database.EnsureInitializedAsync(cancellationToken);
        return new CatalogHealth(true, "SqliteDemo", "Embedded SQLite demo catalog is ready and persisted locally.");
    }

    private static AssetSnapshot Read(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), DemoSqliteDatabase.FromDb(reader.GetString(4)), DemoSqliteDatabase.FromDb(reader.GetString(5)));

    private static string NormalizeTitle(string? title, out string? error)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length == 0) { error = "Asset title is required."; return value; }
        if (value.Length > 300) { error = "Asset title cannot exceed 300 characters."; return value; }
        error = null;
        return value;
    }
}
