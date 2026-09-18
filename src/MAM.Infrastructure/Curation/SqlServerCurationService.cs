using System.Data;
using MAM.Application.Auditing;
using MAM.Application.Curation;
using MAM.Application.Metadata;
using MAM.Domain.Assets;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Curation;

public sealed class SqlServerCurationService : ICurationService
{
    private const int MaxPageSize = 100;
    private const int MaxBulkItems = 100;
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;
    private readonly IMetadataSchemaRegistry _schemas;

    public SqlServerCurationService(SqlServerConnectionFactory connections, IAuditSink audit, IMetadataSchemaRegistry schemas)
    {
        _connections = connections;
        _audit = audit;
        _schemas = schemas;
    }

    public CurationPolicy Policy { get; } = new(
        false,
        "Saved filters are intentionally disabled until owner/site product policy explicitly approves persistence and sharing semantics.",
        MaxPageSize,
        MaxBulkItems);

    public async ValueTask<CurationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = """
                SELECT COUNT_BIG(*)
                FROM dbo.MamSchemaVersion
                WHERE MigrationId = N'0005_p05_search_curation';
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count == 1
                ? new CurationHealth(true, "SqlServer", "P05 authoritative search/curation schema is reachable.")
                : new CurationHealth(false, "SqlServer", "SQL Server is reachable but the P05 search/curation migration is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new CurationHealth(false, "SqlServer", $"Authoritative curation store is unavailable: {ex.GetType().Name}.");
        }
    }

    public async ValueTask<CurationSearchResult> SearchAsync(CurationSearchRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new CurationSearchRequest();
        var search = NormalizeSearch(request);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var where = BuildWhere(search);

        var total = await CountAsync(connection, where, search, cancellationToken);
        var items = new List<CurationAssetItem>();
        var offset = checked((search.Page - 1) * search.PageSize);
        var sql = $"""
            SELECT a.AssetId,
                   a.Title,
                   m.TitleAr,
                   a.Lifecycle,
                   a.Version,
                   m.EventDate,
                   m.Category,
                   COALESCE((SELECT STRING_AGG(t.TagDisplay, N'|') FROM dbo.MamAssetTag t WHERE t.AssetId = a.AssetId), N''),
                   m.PreservationNotes,
                   a.UpdatedAtUtc,
                   (SELECT COUNT(*) FROM dbo.MamCollectionAsset ca WHERE ca.AssetId = a.AssetId)
            FROM dbo.MediaAsset a
            LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId
            {where}
            ORDER BY a.UpdatedAtUtc DESC, a.AssetId ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(command, search);
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            command.Parameters.Add("@PageSize", SqlDbType.Int).Value = search.PageSize;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) items.Add(ReadSearchItem(reader));
        }

        var facets = await ReadFacetsAsync(connection, where, search, cancellationToken);
        return new CurationSearchResult(items, total, search.Page, search.PageSize, facets);
    }

    public async ValueTask<AssetMetadataSnapshot?> GetMetadataAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await ReadMetadataAsync(connection, null, assetId, cancellationToken);
    }

    public async ValueTask<AssetMetadataSnapshot> UpdateMetadataAsync(
        Guid assetId,
        AssetMetadataUpdateRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateMetadataRequest(request);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        const string updateAssetSql = """
            UPDATE dbo.MediaAsset
            SET Title = @Title,
                Version = Version + 1,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE AssetId = @AssetId AND Version = @ExpectedVersion;
            """;
        await using (var command = new SqlCommand(updateAssetSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 300).Value = normalized.TitleEn;
            command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
            command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = normalized.ExpectedVersion;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                var current = await ReadMetadataAsync(connection, transaction, assetId, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                if (current is null) throw new CurationRequestException("asset_not_found", "Asset was not found.", 404);
                throw new CurationRequestException("concurrency_conflict", "Asset changed since it was loaded. Refresh and retry with the current version.", 409, current);
            }
        }

        const string upsertMetadataSql = """
            MERGE dbo.MamAssetMetadata AS target
            USING (SELECT @AssetId AS AssetId) AS source
              ON target.AssetId = source.AssetId
            WHEN MATCHED THEN UPDATE SET
                SchemaKey = @SchemaKey,
                TitleAr = @TitleAr,
                EventDate = @EventDate,
                Category = @Category,
                CategoryNormalized = @CategoryNormalized,
                TagsText = @TagsText,
                PreservationNotes = @PreservationNotes,
                SearchTextNormalized = @SearchTextNormalized,
                UpdatedAtUtc = @UpdatedAtUtc
            WHEN NOT MATCHED THEN INSERT
                (AssetId, SchemaKey, TitleAr, EventDate, Category, CategoryNormalized, TagsText, PreservationNotes, SearchTextNormalized, UpdatedAtUtc)
            VALUES
                (@AssetId, @SchemaKey, @TitleAr, @EventDate, @Category, @CategoryNormalized, @TagsText, @PreservationNotes, @SearchTextNormalized, @UpdatedAtUtc);
            """;
        await using (var command = new SqlCommand(upsertMetadataSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            command.Parameters.Add("@SchemaKey", SqlDbType.NVarChar, 100).Value = normalized.SchemaKey;
            command.Parameters.Add("@TitleAr", SqlDbType.NVarChar, 300).Value = Db(normalized.TitleAr);
            command.Parameters.Add("@EventDate", SqlDbType.Date).Value = normalized.EventDate is null ? DBNull.Value : normalized.EventDate.Value.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Category", SqlDbType.NVarChar, 120).Value = Db(normalized.Category);
            command.Parameters.Add("@CategoryNormalized", SqlDbType.NVarChar, 120).Value = Db(CurationTextNormalizer.NormalizeSearch(normalized.Category));
            command.Parameters.Add("@TagsText", SqlDbType.NVarChar, 1000).Value = Db(string.Join(", ", normalized.Tags));
            command.Parameters.Add("@PreservationNotes", SqlDbType.NVarChar, 2000).Value = Db(normalized.PreservationNotes);
            command.Parameters.Add("@SearchTextNormalized", SqlDbType.NVarChar, 4000).Value = BuildSearchText(normalized);
            command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceTagsAsync(connection, transaction, assetId, normalized.Tags, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var snapshot = await GetMetadataAsync(assetId, cancellationToken)
            ?? throw new CurationRequestException("curation_unavailable", "Updated metadata could not be re-read.", 503);
        await _audit.AppendAsync(NewAudit(actorId, "curation.metadata.updated", "MediaAsset", assetId.ToString("D"), "Success", $"version={snapshot.Version};schema={snapshot.SchemaKey}"), cancellationToken);
        return snapshot;
    }

    public async ValueTask<BulkMetadataResult> BulkUpdateMetadataAsync(
        BulkMetadataRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var items = request?.Items ?? Array.Empty<BulkMetadataItem>();
        if (items.Count == 0) throw new CurationRequestException("bulk_empty", "At least one bulk metadata item is required.", 400);
        if (items.Count > MaxBulkItems) throw new CurationRequestException("bulk_too_large", $"Bulk metadata requests are limited to {MaxBulkItems} assets.", 400);
        if (items.Select(item => item.AssetId).Distinct().Count() != items.Count)
            throw new CurationRequestException("bulk_duplicate_asset", "Each asset can appear only once in a bulk metadata request.", 400);

        var results = new List<BulkMetadataItemResult>(items.Count);
        foreach (var item in items)
        {
            try
            {
                var snapshot = await UpdateMetadataAsync(item.AssetId, new AssetMetadataUpdateRequest(
                    item.ExpectedVersion,
                    item.SchemaKey,
                    item.TitleEn,
                    item.TitleAr,
                    item.EventDate,
                    item.Category,
                    item.Tags,
                    item.PreservationNotes), actorId, cancellationToken);
                results.Add(new BulkMetadataItemResult(item.AssetId, true, "Updated", null, snapshot));
            }
            catch (CurationRequestException ex)
            {
                results.Add(new BulkMetadataItemResult(item.AssetId, false, ex.Code, ex.Message, ex.Current as AssetMetadataSnapshot));
            }
        }
        var succeeded = results.Count(result => result.Succeeded);
        await _audit.AppendAsync(NewAudit(actorId, "curation.metadata.bulk", "BulkMetadata", Guid.NewGuid().ToString("D"), succeeded == items.Count ? "Success" : "Partial", $"requested={items.Count};succeeded={succeeded};failed={items.Count - succeeded}"), cancellationToken);
        return new BulkMetadataResult(items.Count, succeeded, items.Count - succeeded, results);
    }

    public async ValueTask<IReadOnlyList<CollectionSnapshot>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT c.CollectionId, c.NameEn, c.NameAr, c.Version, COUNT(ca.AssetId), c.CreatedAtUtc, c.UpdatedAtUtc
            FROM dbo.MamCollection c
            LEFT JOIN dbo.MamCollectionAsset ca ON ca.CollectionId = c.CollectionId
            GROUP BY c.CollectionId, c.NameEn, c.NameAr, c.Version, c.CreatedAtUtc, c.UpdatedAtUtc
            ORDER BY c.UpdatedAtUtc DESC, c.CollectionId ASC;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var collections = new List<CollectionSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) collections.Add(ReadCollection(reader));
        return collections;
    }

    public async ValueTask<CollectionSnapshot> CreateCollectionAsync(
        CreateCollectionRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var nameEn = RequiredText(request?.NameEn, 200, "Collection English name");
        var nameAr = OptionalText(request?.NameAr, 200, "Collection Arabic name");
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await EnsureCollectionNameAvailableAsync(connection, null, nameEn, null, cancellationToken);
        const string sql = """
            INSERT dbo.MamCollection(CollectionId, NameEn, NameAr, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES(@Id, @NameEn, @NameAr, 1, @CreatedAtUtc, @UpdatedAtUtc);
            """;
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            command.Parameters.Add("@NameEn", SqlDbType.NVarChar, 200).Value = nameEn;
            command.Parameters.Add("@NameAr", SqlDbType.NVarChar, 200).Value = Db(nameAr);
            command.Parameters.Add("@CreatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
            command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var snapshot = new CollectionSnapshot(id, nameEn, nameAr, 1, 0, now, now);
        await _audit.AppendAsync(NewAudit(actorId, "curation.collection.created", "Collection", id.ToString("D"), "Success", "version=1"), cancellationToken);
        return snapshot;
    }

    public async ValueTask<CollectionSnapshot> UpdateCollectionAsync(
        Guid collectionId,
        UpdateCollectionRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.ExpectedVersion < 1)
            throw new CurationRequestException("invalid_collection_version", "A valid expected collection version is required.", 400);
        var nameEn = RequiredText(request.NameEn, 200, "Collection English name");
        var nameAr = OptionalText(request.NameAr, 200, "Collection Arabic name");
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var current = await ReadCollectionAsync(connection, transaction, collectionId, cancellationToken)
            ?? throw new CurationRequestException("collection_not_found", "Collection was not found.", 404);
        if (current.Version != request.ExpectedVersion)
            throw new CurationRequestException("concurrency_conflict", "Collection changed since it was loaded. Refresh and retry.", 409, current);
        await EnsureCollectionNameAvailableAsync(connection, transaction, nameEn, collectionId, cancellationToken);

        const string sql = """
            UPDATE dbo.MamCollection
            SET NameEn=@NameEn,NameAr=@NameAr,Version=Version+1,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE CollectionId=@CollectionId AND Version=@ExpectedVersion;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@NameEn", SqlDbType.NVarChar, 200).Value = nameEn;
            command.Parameters.Add("@NameAr", SqlDbType.NVarChar, 200).Value = Db(nameAr);
            command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new CurationRequestException("concurrency_conflict", "Collection changed while the update was being applied.", 409, current);
        }
        await transaction.CommitAsync(cancellationToken);
        await using var reread = await _connections.OpenAsync(cancellationToken);
        var updated = await ReadCollectionAsync(reread, null, collectionId, cancellationToken)
            ?? throw new CurationRequestException("curation_unavailable", "Updated collection could not be re-read.", 503);
        await _audit.AppendAsync(NewAudit(actorId, "curation.collection.updated", "Collection", collectionId.ToString("D"), "Success", $"version={updated.Version}"), cancellationToken);
        return updated;
    }

    public async ValueTask DeleteCollectionAsync(
        Guid collectionId,
        long expectedVersion,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (expectedVersion < 1)
            throw new CurationRequestException("invalid_collection_version", "A valid expected collection version is required.", 400);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var current = await ReadCollectionAsync(connection, transaction, collectionId, cancellationToken)
            ?? throw new CurationRequestException("collection_not_found", "Collection was not found.", 404);
        if (current.Version != expectedVersion)
            throw new CurationRequestException("concurrency_conflict", "Collection changed since it was loaded. Refresh and retry.", 409, current);

        await using (var members = new SqlCommand("DELETE dbo.MamCollectionAsset WHERE CollectionId=@CollectionId;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            members.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
            await members.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delete = new SqlCommand("DELETE dbo.MamCollection WHERE CollectionId=@CollectionId AND Version=@ExpectedVersion;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            delete.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
            delete.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = expectedVersion;
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new CurationRequestException("concurrency_conflict", "Collection changed while deletion was being applied.", 409, current);
        }
        await transaction.CommitAsync(cancellationToken);
        await _audit.AppendAsync(NewAudit(actorId, "curation.collection.deleted", "Collection", collectionId.ToString("D"), "Success", $"membersDetached={current.MemberCount};assetsDeleted=0"), cancellationToken);
    }

    public ValueTask<CollectionSnapshot> AddToCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(collectionId, assetId, expectedVersion, actorId, true, cancellationToken);

    public ValueTask<CollectionSnapshot> RemoveFromCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(collectionId, assetId, expectedVersion, actorId, false, cancellationToken);

    public async ValueTask<IReadOnlyList<TagSnapshot>> ListTagsAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = CurationTextNormalizer.NormalizeSearch(query);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT t.TagId,t.Name,t.NormalizedName,t.Version,COUNT(at.AssetId),t.CreatedAtUtc,t.UpdatedAtUtc
            FROM dbo.MamTag t
            LEFT JOIN dbo.MamAssetTag at ON at.TagNormalized=t.NormalizedName
            WHERE @Query=N'' OR t.NormalizedName LIKE @LikeQuery ESCAPE N'~'
            GROUP BY t.TagId,t.Name,t.NormalizedName,t.Version,t.CreatedAtUtc,t.UpdatedAtUtc
            ORDER BY COUNT(at.AssetId) DESC,t.Name ASC;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Query", SqlDbType.NVarChar, 120).Value = normalizedQuery;
        command.Parameters.Add("@LikeQuery", SqlDbType.NVarChar, 130).Value = $"%{EscapeLike(normalizedQuery)}%";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TagSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadTag(reader));
        return rows;
    }

    public async ValueTask<TagSnapshot> CreateTagAsync(CreateTagRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var name = ValidateTagName(request?.Name);
        var normalized = CurationTextNormalizer.NormalizeSearch(name);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        if (await ReadTagByNormalizedAsync(connection, null, normalized, cancellationToken) is { } existing)
            throw new CurationRequestException("tag_duplicate", "A tag with the same normalized name already exists.", 409, existing);
        const string sql = """
            INSERT dbo.MamTag(TagId,Name,NormalizedName,Version,CreatedAtUtc,UpdatedAtUtc)
            VALUES(@Id,@Name,@Normalized,1,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 120).Value = name;
        command.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
        var snapshot = new TagSnapshot(id, name, normalized, 1, 0, now, now);
        await _audit.AppendAsync(NewAudit(actorId, "curation.tag.created", "Tag", id.ToString("D"), "Success", $"name={name}"), cancellationToken);
        return snapshot;
    }

    public async ValueTask<TagSnapshot> UpdateTagAsync(Guid tagId, UpdateTagRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (request is null || request.ExpectedVersion < 1)
            throw new CurationRequestException("invalid_tag_version", "A valid expected tag version is required.", 400);
        var name = ValidateTagName(request.Name);
        var normalized = CurationTextNormalizer.NormalizeSearch(name);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var current = await ReadTagAsync(connection, transaction, tagId, cancellationToken)
            ?? throw new CurationRequestException("tag_not_found", "Tag was not found.", 404);
        if (current.Version != request.ExpectedVersion)
            throw new CurationRequestException("concurrency_conflict", "Tag changed since it was loaded. Refresh and retry.", 409, current);
        var duplicate = await ReadTagByNormalizedAsync(connection, transaction, normalized, cancellationToken);
        if (duplicate is not null && duplicate.TagId != tagId)
            throw new CurationRequestException("tag_duplicate", "A tag with the same normalized name already exists.", 409, duplicate);

        var assetIds = await ReadTagAssetIdsAsync(connection, transaction, current.NormalizedName, cancellationToken);
        if (!string.Equals(current.NormalizedName, normalized, StringComparison.Ordinal))
        {
            await using var move = new SqlCommand("UPDATE dbo.MamAssetTag SET TagNormalized=@NewNormalized,TagDisplay=@Name WHERE TagNormalized=@OldNormalized;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            move.Parameters.Add("@NewNormalized", SqlDbType.NVarChar, 120).Value = normalized;
            move.Parameters.Add("@Name", SqlDbType.NVarChar, 120).Value = name;
            move.Parameters.Add("@OldNormalized", SqlDbType.NVarChar, 120).Value = current.NormalizedName;
            await move.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var rename = new SqlCommand("UPDATE dbo.MamAssetTag SET TagDisplay=@Name WHERE TagNormalized=@Normalized;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            rename.Parameters.Add("@Name", SqlDbType.NVarChar, 120).Value = name;
            rename.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
            await rename.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updateSql = """
            UPDATE dbo.MamTag
            SET Name=@Name,NormalizedName=@Normalized,Version=Version+1,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE TagId=@TagId AND Version=@ExpectedVersion;
            """;
        await using (var update = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            update.Parameters.Add("@Name", SqlDbType.NVarChar, 120).Value = name;
            update.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
            update.Parameters.Add("@TagId", SqlDbType.UniqueIdentifier).Value = tagId;
            update.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new CurationRequestException("concurrency_conflict", "Tag changed while the update was being applied.", 409, current);
        }
        foreach (var assetId in assetIds)
            await RefreshAssetTagProjectionAsync(connection, transaction, assetId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using var reread = await _connections.OpenAsync(cancellationToken);
        var updated = await ReadTagAsync(reread, null, tagId, cancellationToken)
            ?? throw new CurationRequestException("curation_unavailable", "Updated tag could not be re-read.", 503);
        await _audit.AppendAsync(NewAudit(actorId, "curation.tag.updated", "Tag", tagId.ToString("D"), "Success", $"assets={assetIds.Count};version={updated.Version}"), cancellationToken);
        return updated;
    }

    public async ValueTask<TagDeletionResult> DeleteTagAsync(Guid tagId, long expectedVersion, bool removeFromAssets, string actorId, CancellationToken cancellationToken = default)
    {
        if (expectedVersion < 1)
            throw new CurationRequestException("invalid_tag_version", "A valid expected tag version is required.", 400);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var current = await ReadTagAsync(connection, transaction, tagId, cancellationToken)
            ?? throw new CurationRequestException("tag_not_found", "Tag was not found.", 404);
        if (current.Version != expectedVersion)
            throw new CurationRequestException("concurrency_conflict", "Tag changed since it was loaded. Refresh and retry.", 409, current);
        if (current.AssetCount > 0 && !removeFromAssets)
            throw new CurationRequestException("tag_in_use", "This tag is assigned to assets. Confirm removal from all assets before deleting it.", 409, current);

        var assetIds = await ReadTagAssetIdsAsync(connection, transaction, current.NormalizedName, cancellationToken);
        if (assetIds.Count > 0)
        {
            await using var detach = new SqlCommand("DELETE dbo.MamAssetTag WHERE TagNormalized=@Normalized;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            detach.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = current.NormalizedName;
            await detach.ExecuteNonQueryAsync(cancellationToken);
            foreach (var assetId in assetIds)
                await RefreshAssetTagProjectionAsync(connection, transaction, assetId, cancellationToken);
        }

        await using (var delete = new SqlCommand("DELETE dbo.MamTag WHERE TagId=@TagId AND Version=@ExpectedVersion;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            delete.Parameters.Add("@TagId", SqlDbType.UniqueIdentifier).Value = tagId;
            delete.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = expectedVersion;
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new CurationRequestException("concurrency_conflict", "Tag changed while deletion was being applied.", 409, current);
        }
        await transaction.CommitAsync(cancellationToken);
        await _audit.AppendAsync(NewAudit(actorId, "curation.tag.deleted", "Tag", tagId.ToString("D"), "Success", $"assetsDetached={assetIds.Count}"), cancellationToken);
        return new TagDeletionResult(tagId, current.Name, assetIds.Count);
    }

    public async ValueTask<AssetMetadataSnapshot> SetArchivedAsync(
        Guid assetId,
        bool archived,
        long expectedVersion,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var desired = archived ? (byte)AssetLifecycleState.Archived : (byte)AssetLifecycleState.Active;
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var current = await ReadMetadataAsync(connection, transaction, assetId, cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CurationRequestException("asset_not_found", "Asset was not found.", 404);
        }
        if (current.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CurationRequestException("concurrency_conflict", "Asset changed since it was loaded. Refresh and retry.", 409, current);
        }
        if (string.Equals(current.Lifecycle, archived ? nameof(AssetLifecycleState.Archived) : nameof(AssetLifecycleState.Active), StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return current;
        }

        const string sql = """
            UPDATE dbo.MediaAsset
            SET Lifecycle = @Lifecycle,
                Version = Version + 1,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE AssetId = @AssetId AND Version = @ExpectedVersion;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@Lifecycle", SqlDbType.TinyInt).Value = desired;
            command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
            command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = expectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                var latest = await ReadMetadataAsync(connection, transaction, assetId, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                throw new CurationRequestException("concurrency_conflict", "Asset changed while lifecycle mutation was being applied.", 409, latest);
            }
        }
        await transaction.CommitAsync(cancellationToken);
        var updated = await GetMetadataAsync(assetId, cancellationToken)
            ?? throw new CurationRequestException("curation_unavailable", "Lifecycle update could not be re-read.", 503);
        await _audit.AppendAsync(NewAudit(actorId, archived ? "curation.asset.archived" : "curation.asset.restored", "MediaAsset", assetId.ToString("D"), "Success", $"version={updated.Version};primary-original=untouched"), cancellationToken);
        return updated;
    }

    private async ValueTask<CollectionSnapshot> MutateMembershipAsync(
        Guid collectionId,
        Guid assetId,
        long expectedVersion,
        string actorId,
        bool add,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var collection = await ReadCollectionAsync(connection, transaction, collectionId, cancellationToken);
        if (collection is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CurationRequestException("collection_not_found", "Collection was not found.", 404);
        }
        if (collection.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CurationRequestException("concurrency_conflict", "Collection changed since it was loaded. Refresh and retry.", 409, collection);
        }
        if (!await AssetExistsAsync(connection, transaction, assetId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new CurationRequestException("asset_not_found", "Asset was not found.", 404);
        }

        var exists = await MembershipExistsAsync(connection, transaction, collectionId, assetId, cancellationToken);
        if (exists == add)
        {
            await transaction.RollbackAsync(cancellationToken);
            return collection;
        }

        var membershipSql = add
            ? "INSERT dbo.MamCollectionAsset(CollectionId, AssetId) VALUES(@CollectionId, @AssetId);"
            : "DELETE dbo.MamCollectionAsset WHERE CollectionId = @CollectionId AND AssetId = @AssetId;";
        await using (var command = new SqlCommand(membershipSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
            command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string bumpSql = """
            UPDATE dbo.MamCollection
            SET Version = Version + 1, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE CollectionId = @CollectionId AND Version = @ExpectedVersion;
            """;
        await using (var command = new SqlCommand(bumpSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = expectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new CurationRequestException("concurrency_conflict", "Collection changed while membership was being updated.", 409);
            }
        }
        await transaction.CommitAsync(cancellationToken);
        await using var readConnection = await _connections.OpenAsync(cancellationToken);
        var updated = await ReadCollectionAsync(readConnection, null, collectionId, cancellationToken)
            ?? throw new CurationRequestException("curation_unavailable", "Updated collection could not be re-read.", 503);
        await _audit.AppendAsync(NewAudit(actorId, add ? "curation.collection.asset-added" : "curation.collection.asset-removed", "Collection", collectionId.ToString("D"), "Success", $"asset={assetId:D};version={updated.Version}"), cancellationToken);
        return updated;
    }

    private MetadataInput ValidateMetadataRequest(AssetMetadataUpdateRequest request)
    {
        if (request is null) throw new CurationRequestException("metadata_required", "Metadata request is required.", 400);
        if (request.ExpectedVersion < 1) throw new CurationRequestException("invalid_expected_version", "ExpectedVersion must be at least 1.", 400);
        var schemaKey = RequiredText(request.SchemaKey, 100, "Schema key");
        var title = RequiredText(request.TitleEn, 300, "English title");
        var titleAr = OptionalText(request.TitleAr, 300, "Arabic title");
        var category = OptionalText(request.Category, 120, "Category");
        var notes = OptionalText(request.PreservationNotes, 2000, "Preservation notes");
        var tags = CurationTextNormalizer.NormalizeTags(request.Tags);
        if (tags.Count > 50) throw new CurationRequestException("too_many_tags", "No more than 50 tags are allowed per asset.", 400);
        if (tags.Any(tag => tag.Length > 120)) throw new CurationRequestException("tag_too_long", "Tags cannot exceed 120 characters.", 400);
        if (tags.Any(tag => tag.Contains('|'))) throw new CurationRequestException("invalid_tag", "Tags cannot contain the '|' separator character.", 400);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = title,
            ["titleAr"] = titleAr,
            ["eventDate"] = request.EventDate?.ToString("yyyy-MM-dd"),
            ["category"] = category,
            ["tags"] = string.Join(", ", tags),
            ["preservationNotes"] = notes
        };
        var errors = _schemas.Validate(schemaKey, values);
        if (errors.Count > 0)
            throw new CurationRequestException("metadata_validation_failed", string.Join("; ", errors.Select(error => $"{error.FieldKey}: {error.Message}")), 400);
        return new MetadataInput(request.ExpectedVersion, schemaKey, title, titleAr, request.EventDate, category, tags, notes);
    }

    private async Task EnsureCollectionNameAvailableAsync(SqlConnection connection, SqlTransaction? transaction, string nameEn, Guid? excludeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(*) FROM dbo.MamCollection
            WHERE LTRIM(RTRIM(NameEn))=@NameEn AND (@ExcludeId IS NULL OR CollectionId<>@ExcludeId);
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@NameEn", SqlDbType.NVarChar, 200).Value = nameEn;
        command.Parameters.Add("@ExcludeId", SqlDbType.UniqueIdentifier).Value = (object?)excludeId ?? DBNull.Value;
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0)
            throw new CurationRequestException("collection_duplicate", "A collection with the same English name already exists.", 409);
    }

    private async ValueTask<TagSnapshot?> ReadTagAsync(SqlConnection connection, SqlTransaction? transaction, Guid tagId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.TagId,t.Name,t.NormalizedName,t.Version,COUNT(at.AssetId),t.CreatedAtUtc,t.UpdatedAtUtc
            FROM dbo.MamTag t LEFT JOIN dbo.MamAssetTag at ON at.TagNormalized=t.NormalizedName
            WHERE t.TagId=@TagId
            GROUP BY t.TagId,t.Name,t.NormalizedName,t.Version,t.CreatedAtUtc,t.UpdatedAtUtc;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@TagId", SqlDbType.UniqueIdentifier).Value = tagId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTag(reader) : null;
    }

    private async ValueTask<TagSnapshot?> ReadTagByNormalizedAsync(SqlConnection connection, SqlTransaction? transaction, string normalized, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.TagId,t.Name,t.NormalizedName,t.Version,COUNT(at.AssetId),t.CreatedAtUtc,t.UpdatedAtUtc
            FROM dbo.MamTag t LEFT JOIN dbo.MamAssetTag at ON at.TagNormalized=t.NormalizedName
            WHERE t.NormalizedName=@Normalized
            GROUP BY t.TagId,t.Name,t.NormalizedName,t.Version,t.CreatedAtUtc,t.UpdatedAtUtc;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTag(reader) : null;
    }

    private async Task<List<Guid>> ReadTagAssetIdsAsync(SqlConnection connection, SqlTransaction transaction, string normalized, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT AssetId FROM dbo.MamAssetTag WHERE TagNormalized=@Normalized ORDER BY AssetId;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task RefreshAssetTagProjectionAsync(SqlConnection connection, SqlTransaction transaction, Guid assetId, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadataAsync(connection, transaction, assetId, cancellationToken);
        if (metadata is null) return;
        var searchText = CurationTextNormalizer.NormalizeSearch(string.Join(' ', new[]
        {
            metadata.TitleEn,
            metadata.TitleAr,
            metadata.Category,
            string.Join(' ', metadata.Tags),
            metadata.PreservationNotes,
            metadata.EventDate?.ToString("yyyy-MM-dd")
        }.Where(value => !string.IsNullOrWhiteSpace(value))));
        const string sql = """
            UPDATE dbo.MamAssetMetadata
            SET TagsText=@TagsText,SearchTextNormalized=@SearchText,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE AssetId=@AssetId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@TagsText", SqlDbType.NVarChar, 1000).Value = Db(string.Join(", ", metadata.Tags));
        command.Parameters.Add("@SearchText", SqlDbType.NVarChar, 4000).Value = searchText;
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReplaceTagsAsync(SqlConnection connection, SqlTransaction transaction, Guid assetId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        await using (var delete = new SqlCommand("DELETE dbo.MamAssetTag WHERE AssetId = @AssetId;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            delete.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var rawTag in tags)
        {
            var tag = ValidateTagName(rawTag);
            var normalized = CurationTextNormalizer.NormalizeSearch(tag);
            var dictionary = await ReadTagByNormalizedAsync(connection, transaction, normalized, cancellationToken);
            if (dictionary is null)
            {
                var id = Guid.NewGuid();
                var now = DateTime.UtcNow;
                await using var create = new SqlCommand("INSERT dbo.MamTag(TagId,Name,NormalizedName,Version,CreatedAtUtc,UpdatedAtUtc) VALUES(@Id,@Name,@Normalized,1,@Now,@Now);", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
                create.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
                create.Parameters.Add("@Name", SqlDbType.NVarChar, 120).Value = tag;
                create.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = normalized;
                create.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now;
                await create.ExecuteNonQueryAsync(cancellationToken);
                dictionary = new TagSnapshot(id, tag, normalized, 1, 0, new DateTimeOffset(now, TimeSpan.Zero), new DateTimeOffset(now, TimeSpan.Zero));
            }
            await using var insert = new SqlCommand("INSERT dbo.MamAssetTag(AssetId, TagNormalized, TagDisplay) VALUES(@AssetId, @Normalized, @Display);", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            insert.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
            insert.Parameters.Add("@Normalized", SqlDbType.NVarChar, 120).Value = dictionary.NormalizedName;
            insert.Parameters.Add("@Display", SqlDbType.NVarChar, 120).Value = dictionary.Name;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async ValueTask<AssetMetadataSnapshot?> ReadMetadataAsync(SqlConnection connection, SqlTransaction? transaction, Guid assetId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.AssetId, a.Title, m.TitleAr, m.EventDate, m.Category,
                   COALESCE((SELECT STRING_AGG(t.TagDisplay, N'|') FROM dbo.MamAssetTag t WHERE t.AssetId = a.AssetId), N''),
                   m.PreservationNotes, a.Lifecycle, a.Version, a.UpdatedAtUtc, COALESCE(m.SchemaKey, N'core-media-v1')
            FROM dbo.MediaAsset a
            LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId
            WHERE a.AssetId = @AssetId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AssetMetadataSnapshot(
            reader.GetGuid(0),
            reader.GetString(10),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            SplitTags(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            LifecycleName(reader.GetByte(7)),
            reader.GetInt64(8),
            Utc(reader.GetDateTime(9)));
    }

    private async Task<long> CountAsync(SqlConnection connection, string where, NormalizedSearch search, CancellationToken cancellationToken)
    {
        var sql = $"SELECT COUNT_BIG(*) FROM dbo.MediaAsset a LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId {where};";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        AddSearchParameters(command, search);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<CurationFacets> ReadFacetsAsync(SqlConnection connection, string where, NormalizedSearch search, CancellationToken cancellationToken)
    {
        var lifecycle = new List<CurationFacetValue>();
        var lifecycleSql = $"SELECT a.Lifecycle, COUNT_BIG(*) FROM dbo.MediaAsset a LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId {where} GROUP BY a.Lifecycle ORDER BY a.Lifecycle;";
        await using (var command = new SqlCommand(lifecycleSql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(command, search);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lifecycle.Add(new CurationFacetValue(LifecycleName(reader.GetByte(0)), reader.GetInt64(1)));
        }

        var categories = new List<CurationFacetValue>();
        var categorySql = $"SELECT m.Category, COUNT_BIG(*) FROM dbo.MediaAsset a LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId {where} AND m.Category IS NOT NULL AND LEN(LTRIM(RTRIM(m.Category))) > 0 GROUP BY m.Category ORDER BY COUNT_BIG(*) DESC, m.Category ASC;";
        await using (var command = new SqlCommand(categorySql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(command, search);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) categories.Add(new CurationFacetValue(reader.GetString(0), reader.GetInt64(1)));
        }

        var tags = new List<CurationFacetValue>();
        var tagSql = $"""
            SELECT t.TagDisplay, COUNT_BIG(DISTINCT t.AssetId)
            FROM dbo.MamAssetTag t
            INNER JOIN dbo.MediaAsset a ON a.AssetId = t.AssetId
            LEFT JOIN dbo.MamAssetMetadata m ON m.AssetId = a.AssetId
            {where}
            GROUP BY t.TagDisplay
            ORDER BY COUNT_BIG(DISTINCT t.AssetId) DESC, t.TagDisplay ASC;
            """;
        await using (var command = new SqlCommand(tagSql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(command, search);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tags.Add(new CurationFacetValue(reader.GetString(0), reader.GetInt64(1)));
        }
        return new CurationFacets(lifecycle, categories, tags);
    }

    private static CurationAssetItem ReadSearchItem(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        LifecycleName(reader.GetByte(3)),
        reader.GetInt64(4),
        reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        SplitTags(reader.GetString(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        Utc(reader.GetDateTime(9)),
        reader.GetInt32(10));

    private async ValueTask<CollectionSnapshot?> ReadCollectionAsync(SqlConnection connection, SqlTransaction? transaction, Guid collectionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.CollectionId, c.NameEn, c.NameAr, c.Version, COUNT(ca.AssetId), c.CreatedAtUtc, c.UpdatedAtUtc
            FROM dbo.MamCollection c
            LEFT JOIN dbo.MamCollectionAsset ca ON ca.CollectionId = c.CollectionId
            WHERE c.CollectionId = @CollectionId
            GROUP BY c.CollectionId, c.NameEn, c.NameAr, c.Version, c.CreatedAtUtc, c.UpdatedAtUtc;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCollection(reader) : null;
    }

    private static CollectionSnapshot ReadCollection(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetInt64(3),
        reader.GetInt32(4),
        Utc(reader.GetDateTime(5)),
        Utc(reader.GetDateTime(6)));

    private static TagSnapshot ReadTag(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetInt32(4),
        Utc(reader.GetDateTime(5)),
        Utc(reader.GetDateTime(6)));

    private async Task<bool> AssetExistsAsync(SqlConnection connection, SqlTransaction transaction, Guid assetId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MediaAsset WHERE AssetId = @AssetId;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private async Task<bool> MembershipExistsAsync(SqlConnection connection, SqlTransaction transaction, Guid collectionId, Guid assetId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MamCollectionAsset WHERE CollectionId = @CollectionId AND AssetId = @AssetId;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = collectionId;
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static NormalizedSearch NormalizeSearch(CurationSearchRequest request)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, MaxPageSize);
        byte? lifecycle = null;
        if (!string.IsNullOrWhiteSpace(request.Lifecycle))
        {
            if (!Enum.TryParse<AssetLifecycleState>(request.Lifecycle.Trim(), true, out var parsed))
                throw new CurationRequestException("invalid_lifecycle", "Lifecycle filter is invalid.", 400);
            lifecycle = (byte)parsed;
        }
        return new NormalizedSearch(
            request.Query?.Trim() ?? string.Empty,
            CurationTextNormalizer.NormalizeSearch(request.Query),
            lifecycle,
            CurationTextNormalizer.NormalizeSearch(request.Category),
            CurationTextNormalizer.NormalizeSearch(request.Tag),
            request.CollectionId,
            page,
            pageSize);
    }

    private static string BuildWhere(NormalizedSearch search)
    {
        var conditions = new List<string> { "1 = 1" };
        if (search.NormalizedQuery.Length > 0)
            conditions.Add("(LOWER(a.Title) LIKE @RawQuery ESCAPE N'~' OR COALESCE(m.SearchTextNormalized, N'') LIKE @NormalizedQuery ESCAPE N'~')");
        if (search.Lifecycle is not null) conditions.Add("a.Lifecycle = @Lifecycle");
        if (search.CategoryNormalized.Length > 0) conditions.Add("COALESCE(m.CategoryNormalized, N'') = @CategoryNormalized");
        if (search.TagNormalized.Length > 0) conditions.Add("EXISTS (SELECT 1 FROM dbo.MamAssetTag filterTag WHERE filterTag.AssetId = a.AssetId AND filterTag.TagNormalized = @TagNormalized)");
        if (search.CollectionId is not null) conditions.Add("EXISTS (SELECT 1 FROM dbo.MamCollectionAsset filterCollection WHERE filterCollection.AssetId = a.AssetId AND filterCollection.CollectionId = @CollectionId)");
        return "WHERE " + string.Join(" AND ", conditions);
    }

    private static void AddSearchParameters(SqlCommand command, NormalizedSearch search)
    {
        if (search.NormalizedQuery.Length > 0)
        {
            command.Parameters.Add("@RawQuery", SqlDbType.NVarChar, 4000).Value = $"%{EscapeLike(search.RawQuery.ToLowerInvariant())}%";
            command.Parameters.Add("@NormalizedQuery", SqlDbType.NVarChar, 4000).Value = $"%{EscapeLike(search.NormalizedQuery)}%";
        }
        if (search.Lifecycle is not null) command.Parameters.Add("@Lifecycle", SqlDbType.TinyInt).Value = search.Lifecycle.Value;
        if (search.CategoryNormalized.Length > 0) command.Parameters.Add("@CategoryNormalized", SqlDbType.NVarChar, 120).Value = search.CategoryNormalized;
        if (search.TagNormalized.Length > 0) command.Parameters.Add("@TagNormalized", SqlDbType.NVarChar, 120).Value = search.TagNormalized;
        if (search.CollectionId is not null) command.Parameters.Add("@CollectionId", SqlDbType.UniqueIdentifier).Value = search.CollectionId.Value;
    }

    private static string EscapeLike(string value) => value.Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal);

    private static string BuildSearchText(MetadataInput input) => CurationTextNormalizer.NormalizeSearch(string.Join(' ', new[]
    {
        input.TitleEn,
        input.TitleAr,
        input.Category,
        string.Join(' ', input.Tags),
        input.PreservationNotes,
        input.EventDate?.ToString("yyyy-MM-dd")
    }.Where(value => !string.IsNullOrWhiteSpace(value))));

    private static IReadOnlyList<string> SplitTags(string value) =>
        string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string LifecycleName(byte value) => Enum.IsDefined(typeof(AssetLifecycleState), (int)value)
        ? ((AssetLifecycleState)value).ToString()
        : $"Unknown({value})";

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string ValidateTagName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 120)
            throw new CurationRequestException("invalid_tag_name", "Tag name is required and must not exceed 120 characters.", 400);
        var normalized = CurationTextNormalizer.NormalizeSearch(name);
        if (normalized.Length == 0)
            throw new CurationRequestException("invalid_tag_name", "Tag name must contain at least one searchable letter or number.", 400);
        return name;
    }

    private static string RequiredText(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new CurationRequestException("validation_failed", $"{label} is required.", 400);
        if (normalized.Length > maxLength) throw new CurationRequestException("validation_failed", $"{label} cannot exceed {maxLength} characters.", 400);
        return normalized;
    }

    private static string? OptionalText(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maxLength) throw new CurationRequestException("validation_failed", $"{label} cannot exceed {maxLength} characters.", 400);
        return normalized;
    }

    private static AuditEvent NewAudit(string actorId, string action, string entityType, string entityId, string outcome, string? detail) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId, action, entityType, entityId, outcome, detail);

    private sealed record MetadataInput(
        long ExpectedVersion,
        string SchemaKey,
        string TitleEn,
        string? TitleAr,
        DateOnly? EventDate,
        string? Category,
        IReadOnlyList<string> Tags,
        string? PreservationNotes);

    private sealed record NormalizedSearch(
        string RawQuery,
        string NormalizedQuery,
        byte? Lifecycle,
        string CategoryNormalized,
        string TagNormalized,
        Guid? CollectionId,
        int Page,
        int PageSize);
}
