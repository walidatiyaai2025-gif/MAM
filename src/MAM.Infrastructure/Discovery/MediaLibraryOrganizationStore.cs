using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.MediaLibrary;
using MAM.Domain.Assets;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Discovery;

public sealed class MediaLibraryOrganizationStore
{
    public static readonly Guid UncategorizedCategoryId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly SqlServerConnectionFactory? _sql;
    private readonly DemoSqliteDatabase? _demo;
    private readonly IAuditSink _audit;

    public MediaLibraryOrganizationStore(SqlServerConnectionFactory? sql, DemoSqliteDatabase? demo, IAuditSink audit)
    {
        _sql = sql;
        _demo = demo;
        _audit = audit;
    }

    public async Task<IReadOnlyList<MediaLibraryAssetSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_demo is not null)
            return await ListDemoAsync(cancellationToken);
        if (_sql is not null)
            return await ListSqlAsync(cancellationToken);
        throw Unavailable();
    }

    public async Task<MediaLibraryAssetSnapshot> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (_demo is not null)
        {
            await EnsureDemoSchemaAsync(cancellationToken);
            await using var connection = await _demo.OpenAsync(cancellationToken);
            return await GetDemoAsync(connection, null, assetId, cancellationToken)
                   ?? throw new MediaLibraryRequestException("asset_not_found", "The requested media asset was not found.", 404);
        }

        if (_sql is not null)
        {
            await using var connection = await _sql.OpenAsync(cancellationToken);
            return await GetSqlAsync(connection, null, assetId, cancellationToken)
                   ?? throw new MediaLibraryRequestException("asset_not_found", "The requested media asset was not found.", 404);
        }

        throw Unavailable();
    }

    public async Task<MediaLibraryAssetSnapshot> UpdateAsync(
        Guid assetId,
        UpdateMediaOrganizationRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedVersion <= 0)
            throw new MediaLibraryRequestException("invalid_version", "ExpectedVersion must be greater than zero.", 400);

        if (_demo is not null)
            return await UpdateDemoAsync(assetId, request, actorId, cancellationToken);
        if (_sql is not null)
            return await UpdateSqlAsync(assetId, request, actorId, cancellationToken);
        throw Unavailable();
    }

    private async Task<IReadOnlyList<MediaLibraryAssetSnapshot>> ListSqlAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _sql!.OpenAsync(cancellationToken);
        const string sql = """
            SELECT a.AssetId,a.Title,a.Lifecycle,a.Version,a.UploadedAtUtc,a.ProductionDate,
                   tm.MediaType,o.OriginalFileName,
                   c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,a.UpdatedAtUtc
            FROM dbo.MediaAsset a
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
            LEFT JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId
            LEFT JOIN dbo.MamAssetCategory ac ON ac.AssetId=a.AssetId
            LEFT JOIN dbo.MamCategory c ON c.CategoryId=ac.CategoryId
            ORDER BY a.UploadedAtUtc DESC,a.AssetId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _sql.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MediaLibraryAssetSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSql(reader));
        return rows;
    }

    private async Task<MediaLibraryAssetSnapshot?> GetSqlAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid assetId,
        CancellationToken cancellationToken,
        bool lockForUpdate = false)
    {
        var lockHint = lockForUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT a.AssetId,a.Title,a.Lifecycle,a.Version,a.UploadedAtUtc,a.ProductionDate,
                   tm.MediaType,o.OriginalFileName,
                   c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,a.UpdatedAtUtc
            FROM dbo.MediaAsset a{lockHint}
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
            LEFT JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId
            LEFT JOIN dbo.MamAssetCategory ac ON ac.AssetId=a.AssetId
            LEFT JOIN dbo.MamCategory c ON c.CategoryId=ac.CategoryId
            WHERE a.AssetId=@AssetId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _sql!.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSql(reader) : null;
    }

    private async Task<MediaLibraryAssetSnapshot> UpdateSqlAsync(
        Guid assetId,
        UpdateMediaOrganizationRequest request,
        string actorId,
        CancellationToken cancellationToken)
    {
        var targetCategoryId = request.CategoryId ?? UncategorizedCategoryId;
        await using var connection = await _sql!.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var current = await GetSqlAsync(connection, transaction, assetId, cancellationToken, lockForUpdate: true)
                      ?? throw new MediaLibraryRequestException("asset_not_found", "The requested media asset was not found.", 404);
        if (current.Version != request.ExpectedVersion)
            throw new MediaLibraryRequestException(
                "concurrency_conflict",
                "The media asset was changed by another request. Refresh and retry with the current version.",
                409,
                current);

        const string categorySql = "SELECT NameEn FROM dbo.MamCategory WHERE CategoryId=@CategoryId;";
        string categoryName;
        await using (var category = new SqlCommand(categorySql, connection, transaction) { CommandTimeout = _sql.CommandTimeoutSeconds })
        {
            category.Parameters.AddWithValue("@CategoryId", targetCategoryId);
            categoryName = Convert.ToString(await category.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(categoryName))
            throw new MediaLibraryRequestException("category_not_found", "The selected category does not exist.", 400, current);

        var now = DateTimeOffset.UtcNow;
        const string updateSql = """
            UPDATE dbo.MediaAsset
            SET ProductionDate=@ProductionDate,
                Version=Version+1,
                UpdatedAtUtc=@UpdatedAtUtc,
                UpdatedByUserId=@ActorId
            WHERE AssetId=@AssetId AND Version=@ExpectedVersion;

            MERGE dbo.MamAssetCategory AS target
            USING (SELECT @AssetId AS AssetId,@CategoryId AS CategoryId) AS source
               ON target.AssetId=source.AssetId
            WHEN MATCHED THEN UPDATE SET CategoryId=source.CategoryId,AssignedBy=@ActorText,AssignedAtUtc=@UpdatedAtUtc
            WHEN NOT MATCHED THEN INSERT(AssetId,CategoryId,AssignedBy,AssignedAtUtc)
                 VALUES(source.AssetId,source.CategoryId,@ActorText,@UpdatedAtUtc);

            UPDATE dbo.MamAssetMetadata
            SET Category=@CategoryName,
                CategoryNormalized=LOWER(@CategoryName),
                UpdatedAtUtc=@UpdatedAtUtc
            WHERE AssetId=@AssetId;
            """;
        await using (var command = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = _sql.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId);
            command.Parameters.AddWithValue("@CategoryId", targetCategoryId);
            command.Parameters.AddWithValue("@CategoryName", categoryName);
            command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
            command.Parameters.AddWithValue("@UpdatedAtUtc", now.UtcDateTime);
            command.Parameters.AddWithValue("@ActorText", SafeActor(actorId));
            command.Parameters.AddWithValue("@ActorId", ActorGuidOrDbNull(actorId));
            var production = command.Parameters.Add("@ProductionDate", SqlDbType.Date);
            production.Value = request.ProductionDate is null
                ? DBNull.Value
                : request.ProductionDate.Value.ToDateTime(TimeOnly.MinValue);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAsync(assetId, cancellationToken);
        await AuditAsync(
            actorId,
            "asset.organization.updated",
            assetId,
            $"oldProductionDate={FormatDate(current.ProductionDate)};newProductionDate={FormatDate(updated.ProductionDate)};oldCategoryId={current.CategoryId:D};newCategoryId={updated.CategoryId:D};version={updated.Version}",
            cancellationToken);
        return updated;
    }

    private static MediaLibraryAssetSnapshot ReadSql(SqlDataReader reader)
    {
        var categoryId = reader.IsDBNull(8) ? UncategorizedCategoryId : reader.GetGuid(8);
        var lifecycleValue = reader.GetByte(2);
        var lifecycle = Enum.IsDefined(typeof(AssetLifecycleState), (int)lifecycleValue)
            ? ((AssetLifecycleState)lifecycleValue).ToString()
            : $"Unknown({lifecycleValue})";
        return new MediaLibraryAssetSnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            lifecycle,
            reader.GetInt64(3),
            Utc(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
            NormalizeMediaKind(reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)),
            categoryId,
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? "Uncategorized" : reader.GetString(10),
            reader.IsDBNull(11) ? (categoryId == UncategorizedCategoryId ? "غير مصنف" : null) : reader.GetString(11),
            Utc(reader.GetDateTime(12)));
    }

    private async Task<IReadOnlyList<MediaLibraryAssetSnapshot>> ListDemoAsync(CancellationToken cancellationToken)
    {
        await EnsureDemoSchemaAsync(cancellationToken);
        await using var connection = await _demo!.OpenAsync(cancellationToken);
        const string sql = """
            SELECT a.AssetId,a.Title,a.Lifecycle,a.Version,a.UploadedAtUtc,a.ProductionDate,a.MediaKind,a.OriginalFileName,
                   c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,a.UpdatedAtUtc
            FROM DemoAsset a
            LEFT JOIN DemoAssetCategory ac ON ac.AssetId=a.AssetId
            LEFT JOIN DemoCategory c ON c.CategoryId=ac.CategoryId
            ORDER BY a.UploadedAtUtc DESC,a.AssetId;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MediaLibraryAssetSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadDemo(reader));
        return rows;
    }

    private async Task<MediaLibraryAssetSnapshot?> GetDemoAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.AssetId,a.Title,a.Lifecycle,a.Version,a.UploadedAtUtc,a.ProductionDate,a.MediaKind,a.OriginalFileName,
                   c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,a.UpdatedAtUtc
            FROM DemoAsset a
            LEFT JOIN DemoAssetCategory ac ON ac.AssetId=a.AssetId
            LEFT JOIN DemoCategory c ON c.CategoryId=ac.CategoryId
            WHERE a.AssetId=$assetId;
            """;
        command.Parameters.AddWithValue("$assetId", assetId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDemo(reader) : null;
    }

    private async Task<MediaLibraryAssetSnapshot> UpdateDemoAsync(
        Guid assetId,
        UpdateMediaOrganizationRequest request,
        string actorId,
        CancellationToken cancellationToken)
    {
        await EnsureDemoSchemaAsync(cancellationToken);
        await using var connection = await _demo!.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var current = await GetDemoAsync(connection, transaction, assetId, cancellationToken)
                      ?? throw new MediaLibraryRequestException("asset_not_found", "The requested media asset was not found.", 404);
        if (current.Version != request.ExpectedVersion)
            throw new MediaLibraryRequestException(
                "concurrency_conflict",
                "The media asset was changed by another request. Refresh and retry with the current version.",
                409,
                current);

        var targetCategoryId = request.CategoryId ?? UncategorizedCategoryId;
        string categoryName;
        await using (var category = connection.CreateCommand())
        {
            category.Transaction = transaction;
            category.CommandText = "SELECT NameEn FROM DemoCategory WHERE CategoryId=$categoryId;";
            category.Parameters.AddWithValue("$categoryId", targetCategoryId.ToString("D"));
            categoryName = Convert.ToString(await category.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(categoryName))
            throw new MediaLibraryRequestException("category_not_found", "The selected category does not exist.", 400, current);

        var now = DateTimeOffset.UtcNow;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE DemoAsset
                SET ProductionDate=$productionDate,Category=$categoryName,Version=Version+1,UpdatedAtUtc=$updated
                WHERE AssetId=$assetId AND Version=$expectedVersion;

                INSERT INTO DemoAssetCategory(AssetId,CategoryId)
                VALUES($assetId,$categoryId)
                ON CONFLICT(AssetId) DO UPDATE SET CategoryId=excluded.CategoryId;
                """;
            command.Parameters.AddWithValue("$productionDate", request.ProductionDate is null ? DBNull.Value : request.ProductionDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$categoryName", categoryName);
            command.Parameters.AddWithValue("$updated", DemoSqliteDatabase.ToDb(now));
            command.Parameters.AddWithValue("$assetId", assetId.ToString("D"));
            command.Parameters.AddWithValue("$categoryId", targetCategoryId.ToString("D"));
            command.Parameters.AddWithValue("$expectedVersion", request.ExpectedVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAsync(assetId, cancellationToken);
        await AuditAsync(
            actorId,
            "asset.organization.updated",
            assetId,
            $"provider=Sqlite;oldProductionDate={FormatDate(current.ProductionDate)};newProductionDate={FormatDate(updated.ProductionDate)};oldCategoryId={current.CategoryId:D};newCategoryId={updated.CategoryId:D};version={updated.Version}",
            cancellationToken);
        return updated;
    }

    private async Task EnsureDemoSchemaAsync(CancellationToken cancellationToken)
    {
        await _demo!.EnsureInitializedAsync(cancellationToken);
        await using var connection = await _demo.OpenAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(DemoAsset);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("UploadedAtUtc"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE DemoAsset ADD COLUMN UploadedAtUtc TEXT NULL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!columns.Contains("ProductionDate"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE DemoAsset ADD COLUMN ProductionDate TEXT NULL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE DemoAsset SET UploadedAtUtc=CreatedAtUtc WHERE UploadedAtUtc IS NULL OR TRIM(UploadedAtUtc)='';
                CREATE INDEX IF NOT EXISTS IX_DemoAsset_UploadedAtUtc ON DemoAsset(UploadedAtUtc DESC,AssetId);
                CREATE INDEX IF NOT EXISTS IX_DemoAsset_ProductionDate ON DemoAsset(ProductionDate DESC,AssetId);
                INSERT OR IGNORE INTO DemoAssetCategory(AssetId,CategoryId)
                SELECT AssetId,'00000000-0000-0000-0000-000000000001' FROM DemoAsset;
                DROP TRIGGER IF EXISTS TR_DemoAsset_DefaultOrganization;
                CREATE TRIGGER TR_DemoAsset_DefaultOrganization AFTER INSERT ON DemoAsset
                BEGIN
                    UPDATE DemoAsset SET UploadedAtUtc=COALESCE(NULLIF(NEW.UploadedAtUtc,''),NEW.CreatedAtUtc) WHERE AssetId=NEW.AssetId;
                    INSERT OR IGNORE INTO DemoAssetCategory(AssetId,CategoryId)
                    VALUES(NEW.AssetId,'00000000-0000-0000-0000-000000000001');
                END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var existing = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CategoryId,NameEn FROM DemoCategory;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existing[NormalizeLegacyCategory(reader.GetString(1))] = Guid.Parse(reader.GetString(0));
        }

        var legacy = new List<(Guid AssetId, string Category)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.AssetId,a.Category
                FROM DemoAsset a
                LEFT JOIN DemoAssetCategory ac ON ac.AssetId=a.AssetId
                WHERE a.Category IS NOT NULL AND TRIM(a.Category)<>''
                  AND (ac.CategoryId IS NULL OR ac.CategoryId='00000000-0000-0000-0000-000000000001');
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                legacy.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1).Trim()));
        }

        foreach (var row in legacy)
        {
            var normalized = NormalizeLegacyCategory(row.Category);
            if (normalized.Length == 0 || normalized == "uncategorized") continue;
            if (!existing.TryGetValue(normalized, out var categoryId))
            {
                categoryId = DeterministicCategoryId(normalized);
                await using var create = connection.CreateCommand();
                create.CommandText = """
                    INSERT OR IGNORE INTO DemoCategory(CategoryId,ParentCategoryId,NameEn,NameAr,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc)
                    VALUES($id,NULL,$name,NULL,0,0,1,$now,$now);
                    """;
                create.Parameters.AddWithValue("$id", categoryId.ToString("D"));
                create.Parameters.AddWithValue("$name", row.Category.Length > 200 ? row.Category[..200] : row.Category);
                create.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));
                await create.ExecuteNonQueryAsync(cancellationToken);
                existing[normalized] = categoryId;
            }

            await using var assign = connection.CreateCommand();
            assign.CommandText = """
                INSERT INTO DemoAssetCategory(AssetId,CategoryId) VALUES($assetId,$categoryId)
                ON CONFLICT(AssetId) DO UPDATE SET CategoryId=excluded.CategoryId
                WHERE DemoAssetCategory.CategoryId='00000000-0000-0000-0000-000000000001';
                """;
            assign.Parameters.AddWithValue("$assetId", row.AssetId.ToString("D"));
            assign.Parameters.AddWithValue("$categoryId", categoryId.ToString("D"));
            await assign.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static MediaLibraryAssetSnapshot ReadDemo(SqliteDataReader reader)
    {
        var categoryId = reader.IsDBNull(8) ? UncategorizedCategoryId : Guid.Parse(reader.GetString(8));
        return new MediaLibraryAssetSnapshot(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            DemoSqliteDatabase.FromDb(reader.GetString(4)),
            reader.IsDBNull(5) || string.IsNullOrWhiteSpace(reader.GetString(5)) ? null : DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            NormalizeMediaKind(reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)),
            categoryId,
            reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? "Uncategorized" : reader.GetString(10),
            reader.IsDBNull(11) ? (categoryId == UncategorizedCategoryId ? "غير مصنف" : null) : reader.GetString(11),
            DemoSqliteDatabase.FromDb(reader.GetString(12)));
    }

    private async Task AuditAsync(string actorId, string action, Guid assetId, string detail, CancellationToken cancellationToken) =>
        await _audit.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            SafeActor(actorId),
            action,
            "MediaAsset",
            assetId.ToString("D"),
            "Success",
            detail), cancellationToken);

    private static string NormalizeMediaKind(string? technicalKind, string? fileName)
    {
        var value = technicalKind?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (value.Equals(MediaKinds.Video, StringComparison.OrdinalIgnoreCase)) return MediaKinds.Video;
            if (value.Equals(MediaKinds.Audio, StringComparison.OrdinalIgnoreCase)) return MediaKinds.Audio;
            if (value.Equals(MediaKinds.Image, StringComparison.OrdinalIgnoreCase)) return MediaKinds.Image;
            if (value.Equals(MediaKinds.Document, StringComparison.OrdinalIgnoreCase)) return MediaKinds.Document;
            if (value.Equals(MediaKinds.Other, StringComparison.OrdinalIgnoreCase)) return MediaKinds.Other;
        }
        return MediaKinds.FromFileName(fileName);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null";
    private static string SafeActor(string? actorId) => string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim()[..Math.Min(actorId.Trim().Length, 200)];
    private static object ActorGuidOrDbNull(string? actorId) => Guid.TryParse(actorId, out var actor) ? actor : DBNull.Value;
    private static string NormalizeLegacyCategory(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static Guid DeterministicCategoryId(string normalized)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("mam-legacy-category:" + normalized));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static MediaLibraryRequestException Unavailable() =>
        new("media_library_unavailable", "Media Library organization requires the authoritative SQL Server store or Offline Demo SQLite store.", 503);
}
