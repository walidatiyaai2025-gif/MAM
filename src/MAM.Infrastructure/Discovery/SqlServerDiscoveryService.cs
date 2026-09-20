using System.Globalization;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Discovery;

public sealed class SqlServerDiscoveryService : IDiscoveryService
{
    public static readonly Guid UncategorizedCategoryId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;

    public SqlServerDiscoveryService(SqlServerConnectionFactory connections, IAuditSink audit)
    {
        _connections = connections;
        _audit = audit;
    }

    public async Task<DiscoveryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0008_p12_discovery_ai_indexing';";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            var ready = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
            return ready
                ? new DiscoveryHealth(true, "SqlServer", "P12 discovery, categories and extracted-text index are ready.")
                : new DiscoveryHealth(false, "SqlServer", "P12 discovery migration is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new DiscoveryHealth(false, "SqlServer", $"Discovery dependency unavailable: {ex.GetType().Name}.");
        }
    }

    public async Task<IReadOnlyList<CategorySnapshot>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,c.IsSystem,c.SortOrder,c.Version,
                   (SELECT COUNT_BIG(*) FROM dbo.MamAssetCategory ac WHERE ac.CategoryId=c.CategoryId) AssetCount,
                   (SELECT COUNT_BIG(*) FROM dbo.MamCategory child WHERE child.ParentCategoryId=c.CategoryId) ChildCount,
                   c.CreatedAtUtc,c.UpdatedAtUtc
            FROM dbo.MamCategory c
            ORDER BY CASE WHEN c.IsSystem=1 THEN 0 ELSE 1 END,c.SortOrder,c.NameEn,c.CategoryId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CategorySnapshot>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCategory(reader));
        return result;
    }

    public async Task<CategorySnapshot> CreateCategoryAsync(CreateCategoryRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var name = Required(request.NameEn, "category_name_required", "English category name is required.", 200);
        var nameAr = Optional(request.NameAr, 200);
        await ValidateParentAsync(request.ParentCategoryId, null, cancellationToken);
        var id = Guid.NewGuid();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            INSERT dbo.MamCategory(CategoryId,ParentCategoryId,NameEn,NameAr,NameNormalized,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc)
            VALUES(@Id,@Parent,@NameEn,@NameAr,@Normalized,0,@SortOrder,1,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Parent", (object?)request.ParentCategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@NameEn", name);
        command.Parameters.AddWithValue("@NameAr", (object?)nameAr ?? DBNull.Value);
        command.Parameters.AddWithValue("@Normalized", DiscoveryText.Normalize(name));
        command.Parameters.AddWithValue("@SortOrder", request.SortOrder);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw Error("category_duplicate", "A category with this name already exists under the selected parent.", 409); }
        await AuditAsync(actorId, "category.created", id, name, cancellationToken);
        return await GetCategoryAsync(id, cancellationToken);
    }

    public async Task<CategorySnapshot> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (categoryId == UncategorizedCategoryId) throw Error("system_category_read_only", "The Uncategorized system category cannot be edited.", 409);
        var name = Required(request.NameEn, "category_name_required", "English category name is required.", 200);
        var nameAr = Optional(request.NameAr, 200);
        await ValidateParentAsync(request.ParentCategoryId, categoryId, cancellationToken);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamCategory SET ParentCategoryId=@Parent,NameEn=@NameEn,NameAr=@NameAr,NameNormalized=@Normalized,
                SortOrder=@SortOrder,Version=Version+1,UpdatedAtUtc=@Now
            WHERE CategoryId=@Id AND IsSystem=0 AND Version=@ExpectedVersion;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Parent", (object?)request.ParentCategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@NameEn", name);
        command.Parameters.AddWithValue("@NameAr", (object?)nameAr ?? DBNull.Value);
        command.Parameters.AddWithValue("@Normalized", DiscoveryText.Normalize(name));
        command.Parameters.AddWithValue("@SortOrder", request.SortOrder);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@Id", categoryId);
        command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw Error("category_concurrency_conflict", "Category changed or no longer exists.", 409);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw Error("category_duplicate", "A category with this name already exists under the selected parent.", 409); }
        await AuditAsync(actorId, "category.updated", categoryId, name, cancellationToken);
        return await GetCategoryAsync(categoryId, cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, string actorId, CancellationToken cancellationToken = default)
    {
        if (categoryId == UncategorizedCategoryId) throw Error("system_category_read_only", "The Uncategorized system category cannot be deleted.", 409);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string checkSql = "SELECT (SELECT COUNT_BIG(*) FROM dbo.MamCategory WHERE ParentCategoryId=@Id)+(SELECT COUNT_BIG(*) FROM dbo.MamAssetCategory WHERE CategoryId=@Id);";
        await using (var check = new SqlCommand(checkSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            check.Parameters.AddWithValue("@Id", categoryId);
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
                throw Error("category_not_empty", "Move child categories and assets before deleting this category.", 409);
        }
        await using (var delete = new SqlCommand("DELETE dbo.MamCategory WHERE CategoryId=@Id AND IsSystem=0;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            delete.Parameters.AddWithValue("@Id", categoryId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1) throw Error("category_not_found", "Category was not found.", 404);
        }
        await transaction.CommitAsync(cancellationToken);
        await AuditAsync(actorId, "category.deleted", categoryId, null, cancellationToken);
    }

    public async Task<AssetCategorySnapshot> GetAssetCategoryAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId, cancellationToken);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP(1) c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,c.IsSystem,c.SortOrder,c.Version,
                   (SELECT COUNT_BIG(*) FROM dbo.MamAssetCategory x WHERE x.CategoryId=c.CategoryId),
                   (SELECT COUNT_BIG(*) FROM dbo.MamCategory child WHERE child.ParentCategoryId=c.CategoryId),
                   c.CreatedAtUtc,c.UpdatedAtUtc
            FROM dbo.MamCategory c
            LEFT JOIN dbo.MamAssetCategory ac ON ac.CategoryId=c.CategoryId AND ac.AssetId=@AssetId
            WHERE ac.AssetId IS NOT NULL OR (c.CategoryId=@Uncategorized AND NOT EXISTS(SELECT 1 FROM dbo.MamAssetCategory z WHERE z.AssetId=@AssetId))
            ORDER BY CASE WHEN ac.AssetId IS NOT NULL THEN 0 ELSE 1 END;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        command.Parameters.AddWithValue("@Uncategorized", UncategorizedCategoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Error("uncategorized_missing", "Uncategorized system category is unavailable.", 503);
        return new AssetCategorySnapshot(assetId, ReadCategory(reader));
    }

    public async Task<AssetCategorySnapshot> AssignAssetCategoryAsync(Guid assetId, Guid? categoryId, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId, cancellationToken);
        var resolved = categoryId ?? UncategorizedCategoryId;
        var category = await GetCategoryAsync(resolved, cancellationToken);
        var now = DateTime.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        const string assignmentSql = """
            MERGE dbo.MamAssetCategory AS target
            USING (SELECT @AssetId AssetId) AS source ON target.AssetId=source.AssetId
            WHEN MATCHED THEN UPDATE SET CategoryId=@CategoryId,AssignedBy=@Actor,AssignedAtUtc=@Now
            WHEN NOT MATCHED THEN INSERT(AssetId,CategoryId,AssignedBy,AssignedAtUtc) VALUES(@AssetId,@CategoryId,@Actor,@Now);
            """;
        await using (var command = new SqlCommand(assignmentSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId);
            command.Parameters.AddWithValue("@CategoryId", resolved);
            command.Parameters.AddWithValue("@Actor", SafeActor(actorId));
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // MamAssetCategory is authoritative, but keep the legacy curation category
        // projection synchronized so every Web/Desktop metadata surface shows the
        // same folder-derived classification immediately.
        const string metadataSql = """
            UPDATE dbo.MamAssetMetadata
            SET Category=@CategoryName,CategoryNormalized=@CategoryNormalized,UpdatedAtUtc=@Now
            WHERE AssetId=@AssetId;
            """;
        await using (var command = new SqlCommand(metadataSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId);
            command.Parameters.AddWithValue("@CategoryName", category.NameEn);
            command.Parameters.AddWithValue("@CategoryNormalized", DiscoveryText.Normalize(category.NameEn));
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await AuditAsync(actorId, "asset.category.assigned", assetId, resolved.ToString("D"), cancellationToken);
        return await GetAssetCategoryAsync(assetId, cancellationToken);
    }

    public async Task UpsertTextAsync(Guid assetId, string sourceKind, string? language, string text, string? contentSha256, IReadOnlyList<TextSegmentSnapshot>? segments, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId, cancellationToken);
        sourceKind = Required(sourceKind, "source_kind_required", "Text source kind is required.", 40).ToLowerInvariant();
        text ??= string.Empty;
        var normalized = DiscoveryText.Normalize(text);
        var tokens = DiscoveryText.Tokenize(text);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string upsert = """
            MERGE dbo.MamAssetSearchContent AS target
            USING (SELECT @AssetId AssetId,@SourceKind SourceKind) AS source
              ON target.AssetId=source.AssetId AND target.SourceKind=source.SourceKind
            WHEN MATCHED THEN UPDATE SET Language=@Language,ContentText=@Text,SearchTextNormalized=@Normalized,ContentSha256=@Sha,UpdatedAtUtc=@Now
            WHEN NOT MATCHED THEN INSERT(AssetId,SourceKind,Language,ContentText,SearchTextNormalized,ContentSha256,UpdatedAtUtc)
                VALUES(@AssetId,@SourceKind,@Language,@Text,@Normalized,@Sha,@Now);
            DELETE dbo.MamAssetSearchToken WHERE AssetId=@AssetId AND SourceKind=@SourceKind;
            DELETE dbo.MamAssetTextSegment WHERE AssetId=@AssetId AND SourceKind=@SourceKind;
            """;
        await using (var command = new SqlCommand(upsert, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId);
            command.Parameters.AddWithValue("@SourceKind", sourceKind);
            command.Parameters.AddWithValue("@Language", (object?)Optional(language, 20) ?? DBNull.Value);
            command.Parameters.AddWithValue("@Text", text);
            command.Parameters.AddWithValue("@Normalized", normalized);
            command.Parameters.AddWithValue("@Sha", (object?)Optional(contentSha256, 64) ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var token in tokens)
        {
            await using var command = new SqlCommand("INSERT dbo.MamAssetSearchToken(AssetId,SourceKind,TokenNormalized,OccurrenceCount) VALUES(@AssetId,@SourceKind,@Token,@Count);", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind);
            command.Parameters.AddWithValue("@Token", token.Key); command.Parameters.AddWithValue("@Count", token.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (segments is not null)
        {
            foreach (var segment in segments.OrderBy(x => x.SegmentIndex))
            {
                await using var command = new SqlCommand("""
                    INSERT dbo.MamAssetTextSegment(AssetId,SourceKind,SegmentIndex,StartMs,EndMs,PageNumber,Text,TextNormalized)
                    VALUES(@AssetId,@SourceKind,@Index,@Start,@End,@Page,@Text,@Normalized);
                    """, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
                command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind);
                command.Parameters.AddWithValue("@Index", segment.SegmentIndex); command.Parameters.AddWithValue("@Start", (object?)segment.StartMs ?? DBNull.Value);
                command.Parameters.AddWithValue("@End", (object?)segment.EndMs ?? DBNull.Value); command.Parameters.AddWithValue("@Page", (object?)segment.PageNumber ?? DBNull.Value);
                command.Parameters.AddWithValue("@Text", segment.Text ?? string.Empty); command.Parameters.AddWithValue("@Normalized", DiscoveryText.Normalize(segment.Text));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AssetTextSnapshot?> GetTextAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default)
    {
        sourceKind = Required(sourceKind, "source_kind_required", "Text source kind is required.", 40).ToLowerInvariant();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT Language,ContentText,ContentSha256,UpdatedAtUtc FROM dbo.MamAssetSearchContent WHERE AssetId=@AssetId AND SourceKind=@SourceKind;";
        string? language; string text; string? sha; DateTimeOffset updated;
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            language = reader.IsDBNull(0) ? null : reader.GetString(0); text = reader.GetString(1); sha = reader.IsDBNull(2) ? null : reader.GetString(2); updated = Utc(reader.GetDateTime(3));
        }
        var segments = new List<TextSegmentSnapshot>();
        const string segmentSql = "SELECT SegmentIndex,StartMs,EndMs,PageNumber,Text FROM dbo.MamAssetTextSegment WHERE AssetId=@AssetId AND SourceKind=@SourceKind ORDER BY SegmentIndex;";
        await using (var command = new SqlCommand(segmentSql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@SourceKind", sourceKind);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) segments.Add(new TextSegmentSnapshot(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4)));
        }
        return new AssetTextSnapshot(assetId, sourceKind, language, text, sha, segments, updated);
    }

    public async Task SetExtractionStatusAsync(Guid assetId, string extractionKind, string state, int progressPercent, string? detail, bool completed, CancellationToken cancellationToken = default)
    {
        extractionKind = Required(extractionKind, "extraction_kind_required", "Extraction kind is required.", 40).ToLowerInvariant();
        state = Required(state, "extraction_state_required", "Extraction state is required.", 20);
        progressPercent = Math.Clamp(progressPercent, 0, 100);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            MERGE dbo.MamTextExtractionStatus AS target
            USING (SELECT @AssetId AssetId,@Kind ExtractionKind) source ON target.AssetId=source.AssetId AND target.ExtractionKind=source.ExtractionKind
            WHEN MATCHED THEN UPDATE SET State=@State,ProgressPercent=@Progress,Detail=@Detail,UpdatedAtUtc=@Now,CompletedAtUtc=CASE WHEN @Completed=1 THEN @Now ELSE NULL END
            WHEN NOT MATCHED THEN INSERT(AssetId,ExtractionKind,State,ProgressPercent,Detail,UpdatedAtUtc,CompletedAtUtc)
                VALUES(@AssetId,@Kind,@State,@Progress,@Detail,@Now,CASE WHEN @Completed=1 THEN @Now ELSE NULL END);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@Kind", extractionKind); command.Parameters.AddWithValue("@State", state);
        command.Parameters.AddWithValue("@Progress", progressPercent); command.Parameters.AddWithValue("@Detail", (object?)Optional(detail, 300) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Completed", completed); command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TextExtractionStatusSnapshot>> GetExtractionStatusAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT AssetId,ExtractionKind,State,ProgressPercent,Detail,UpdatedAtUtc,CompletedAtUtc FROM dbo.MamTextExtractionStatus WHERE AssetId=@AssetId ORDER BY ExtractionKind;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TextExtractionStatusSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new TextExtractionStatusSnapshot(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3), reader.IsDBNull(4) ? null : reader.GetString(4), Utc(reader.GetDateTime(5)), reader.IsDBNull(6) ? null : Utc(reader.GetDateTime(6))));
        return rows;
    }

    public async Task<DiscoverySearchResult> SearchAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = DiscoveryText.Normalize(request.Query);
        if (query.Length < 2) throw Error("search_query_too_short", "Enter at least two searchable characters.");
        var page = Math.Max(1, request.Page); var pageSize = Math.Clamp(request.PageSize, 1, 100); var offset = (page - 1) * pageSize;
        var tokens = DiscoveryText.Tokenize(query).Keys.Take(12).ToArray();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var tokenWhere = tokens.Length == 0 ? "1=0" : string.Join(" OR ", tokens.Select((_, i) => $"t.TokenNormalized=@Token{i}"));
        var mediaFilter = string.IsNullOrWhiteSpace(request.MediaKind) ? string.Empty : " AND COALESCE(tm.MediaType,N'Other')=@MediaKind";
        var categoryFilter = request.CategoryId is null ? string.Empty : " AND COALESCE(ac.CategoryId,@Uncategorized)=@CategoryId";
        var where = $"""
            (
              EXISTS(SELECT 1 FROM dbo.MamAssetSearchToken t WHERE t.AssetId=a.AssetId AND ({tokenWhere}))
              OR LOWER(a.Title) LIKE N'%' + @RawQuery + N'%'
              OR EXISTS(SELECT 1 FROM dbo.MamAssetMetadata m WHERE m.AssetId=a.AssetId AND m.SearchTextNormalized LIKE N'%' + @RawQuery + N'%')
              OR EXISTS(SELECT 1 FROM dbo.MamAssetReferenceTag art JOIN dbo.MamReferenceSubject rs ON rs.SubjectId=art.SubjectId WHERE art.AssetId=a.AssetId AND LOWER(rs.NameEn+N' '+COALESCE(rs.NameAr,N'')+N' '+COALESCE(rs.TagsText,N'')) LIKE N'%' + @RawQuery + N'%')
            )
            AND NOT EXISTS(SELECT 1 FROM dbo.inv_tape_attachments privateTapeAttachment WHERE privateTapeAttachment.AssetId=a.AssetId)
            {mediaFilter} {categoryFilter}
            """;
        var countSql = $"""
            SELECT COUNT_BIG(*) FROM dbo.MediaAsset a
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
            LEFT JOIN dbo.MamAssetCategory ac ON ac.AssetId=a.AssetId
            WHERE {where};
            """;
        long total;
        await using (var count = new SqlCommand(countSql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(count, query, tokens, request, UncategorizedCategoryId);
            total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        var sql = $"""
            SELECT a.AssetId,a.Title,COALESCE(tm.MediaType,N'Other'),COALESCE(c.CategoryId,@Uncategorized),COALESCE(c.NameEn,N'Uncategorized'),COALESCE(c.NameAr,N'غير مصنف'),
                a.UpdatedAtUtc,
                COALESCE((SELECT TOP(1) sc.SourceKind FROM dbo.MamAssetSearchContent sc WHERE sc.AssetId=a.AssetId AND sc.SearchTextNormalized LIKE N'%' + @RawQuery + N'%' ORDER BY CASE sc.SourceKind WHEN N'transcript' THEN 0 WHEN N'ocr' THEN 1 ELSE 2 END),N'metadata') MatchedSource,
                COALESCE((SELECT TOP(1) LEFT(seg.Text,600) FROM dbo.MamAssetTextSegment seg WHERE seg.AssetId=a.AssetId AND seg.TextNormalized LIKE N'%' + @RawQuery + N'%' ORDER BY seg.SourceKind,seg.SegmentIndex),a.Title) Snippet,
                (SELECT TOP(1) seg.StartMs FROM dbo.MamAssetTextSegment seg WHERE seg.AssetId=a.AssetId AND seg.TextNormalized LIKE N'%' + @RawQuery + N'%' ORDER BY seg.SourceKind,seg.SegmentIndex) StartMs,
                (SELECT TOP(1) seg.EndMs FROM dbo.MamAssetTextSegment seg WHERE seg.AssetId=a.AssetId AND seg.TextNormalized LIKE N'%' + @RawQuery + N'%' ORDER BY seg.SourceKind,seg.SegmentIndex) EndMs,
                (SELECT TOP(1) seg.PageNumber FROM dbo.MamAssetTextSegment seg WHERE seg.AssetId=a.AssetId AND seg.TextNormalized LIKE N'%' + @RawQuery + N'%' ORDER BY seg.SourceKind,seg.SegmentIndex) PageNumber
            FROM dbo.MediaAsset a
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
            LEFT JOIN dbo.MamAssetCategory ac ON ac.AssetId=a.AssetId
            LEFT JOIN dbo.MamCategory c ON c.CategoryId=COALESCE(ac.CategoryId,@Uncategorized)
            WHERE {where}
            ORDER BY a.UpdatedAtUtc DESC,a.AssetId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var result = new List<DiscoverySearchHit>();
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            AddSearchParameters(command, query, tokens, request, UncategorizedCategoryId); command.Parameters.AddWithValue("@Offset", offset); command.Parameters.AddWithValue("@PageSize", pageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var assetId = reader.GetGuid(0);
                result.Add(new DiscoverySearchHit(assetId, reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), Array.Empty<string>(), Utc(reader.GetDateTime(6))));
            }
        }
        for (var i = 0; i < result.Count; i++)
        {
            var tags = await ListAssetReferenceTagsAsync(result[i].AssetId, cancellationToken);
            result[i] = result[i] with { ReferenceTags = tags.Select(t => t.NameEn).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
        }
        return new DiscoverySearchResult(result, total, page, pageSize, query);
    }

    public async Task<IReadOnlyList<ReferenceSubjectSnapshot>> ListReferenceSubjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT SubjectId,NameEn,NameAr,DescriptionEn,DescriptionAr,TagsText,IsActive,CreatedAtUtc,UpdatedAtUtc FROM dbo.MamReferenceSubject ORDER BY NameEn,SubjectId;";
        var rows = new List<ReferenceSubjectSnapshot>();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            rows.Add(new ReferenceSubjectSnapshot(id,reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),SplitTags(reader.IsDBNull(5)?null:reader.GetString(5)),reader.GetBoolean(6),Array.Empty<Guid>(),Utc(reader.GetDateTime(7)),Utc(reader.GetDateTime(8))));
        }
        for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { ReferenceAssetIds = await ListReferenceImageIdsAsync(rows[i].SubjectId, cancellationToken) };
        return rows;
    }

    public async Task<ReferenceSubjectSnapshot> CreateReferenceSubjectAsync(CreateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid(); var name = Required(request.NameEn,"reference_name_required","English reference name is required.",200);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "INSERT dbo.MamReferenceSubject(SubjectId,NameEn,NameAr,DescriptionEn,DescriptionAr,TagsText,IsActive,CreatedAtUtc,UpdatedAtUtc) VALUES(@Id,@NameEn,@NameAr,@DescriptionEn,@DescriptionAr,@Tags,1,@Now,@Now);";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Id",id); command.Parameters.AddWithValue("@NameEn",name); command.Parameters.AddWithValue("@NameAr",(object?)Optional(request.NameAr,200)??DBNull.Value);
        command.Parameters.AddWithValue("@DescriptionEn",(object?)Optional(request.DescriptionEn,1000)??DBNull.Value); command.Parameters.AddWithValue("@DescriptionAr",(object?)Optional(request.DescriptionAr,1000)??DBNull.Value);
        command.Parameters.AddWithValue("@Tags",(object?)JoinTags(request.Tags)??DBNull.Value); command.Parameters.AddWithValue("@Now",DateTime.UtcNow); await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(actorId,"reference.subject.created",id,name,cancellationToken);
        return (await ListReferenceSubjectsAsync(cancellationToken)).Single(x=>x.SubjectId==id);
    }

    public async Task<ReferenceSubjectSnapshot> AddReferenceImageAsync(Guid subjectId, Guid assetId, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken); _ = (await ListReferenceSubjectsAsync(cancellationToken)).FirstOrDefault(x=>x.SubjectId==subjectId) ?? throw Error("reference_subject_not_found","Reference subject was not found.",404);
        var mediaKind = await GetAssetMediaKindAsync(assetId,cancellationToken); if (!string.Equals(mediaKind,MediaKinds.Image,StringComparison.OrdinalIgnoreCase)) throw Error("reference_image_required","Reference library entries must use image assets.",415);
        await using var connection=await _connections.OpenAsync(cancellationToken); const string sql="IF NOT EXISTS(SELECT 1 FROM dbo.MamReferenceImage WHERE SubjectId=@SubjectId AND AssetId=@AssetId) INSERT dbo.MamReferenceImage(SubjectId,AssetId) VALUES(@SubjectId,@AssetId);";
        await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds}; command.Parameters.AddWithValue("@SubjectId",subjectId); command.Parameters.AddWithValue("@AssetId",assetId); await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(actorId,"reference.image.added",subjectId,assetId.ToString("D"),cancellationToken); return (await ListReferenceSubjectsAsync(cancellationToken)).Single(x=>x.SubjectId==subjectId);
    }

    public async Task<AssetReferenceTagSnapshot> AddAssetReferenceTagAsync(Guid assetId, AddAssetReferenceTagRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken); _=(await ListReferenceSubjectsAsync(cancellationToken)).FirstOrDefault(x=>x.SubjectId==request.SubjectId)??throw Error("reference_subject_not_found","Reference subject was not found.",404);
        var source=Required(request.DetectionSource,"reference_detection_source_required","Detection source is required.",40).ToLowerInvariant();
        await using var connection=await _connections.OpenAsync(cancellationToken); const string sql="""
            MERGE dbo.MamAssetReferenceTag target USING(SELECT @AssetId AssetId,@SubjectId SubjectId) source ON target.AssetId=source.AssetId AND target.SubjectId=source.SubjectId
            WHEN MATCHED THEN UPDATE SET Confidence=@Confidence,DetectionSource=@Source,CreatedAtUtc=@Now
            WHEN NOT MATCHED THEN INSERT(AssetId,SubjectId,Confidence,DetectionSource,CreatedAtUtc) VALUES(@AssetId,@SubjectId,@Confidence,@Source,@Now);
            """;
        await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds}; command.Parameters.AddWithValue("@AssetId",assetId);command.Parameters.AddWithValue("@SubjectId",request.SubjectId);command.Parameters.AddWithValue("@Confidence",(object?)request.Confidence??DBNull.Value);command.Parameters.AddWithValue("@Source",source);command.Parameters.AddWithValue("@Now",DateTime.UtcNow);await command.ExecuteNonQueryAsync(cancellationToken);
        await ReindexReferenceTagsAsync(assetId,cancellationToken); await AuditAsync(actorId,"asset.reference.tagged",assetId,request.SubjectId.ToString("D"),cancellationToken); return (await ListAssetReferenceTagsAsync(assetId,cancellationToken)).Single(x=>x.SubjectId==request.SubjectId);
    }

    public async Task<IReadOnlyList<AssetReferenceTagSnapshot>> ListAssetReferenceTagsAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection=await _connections.OpenAsync(cancellationToken); const string sql="SELECT art.AssetId,art.SubjectId,rs.NameEn,rs.NameAr,art.Confidence,art.DetectionSource,art.CreatedAtUtc FROM dbo.MamAssetReferenceTag art JOIN dbo.MamReferenceSubject rs ON rs.SubjectId=art.SubjectId WHERE art.AssetId=@AssetId ORDER BY rs.NameEn,art.SubjectId;";
        await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@AssetId",assetId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<AssetReferenceTagSnapshot>();while(await reader.ReadAsync(cancellationToken))rows.Add(new AssetReferenceTagSnapshot(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetDecimal(4),reader.GetString(5),Utc(reader.GetDateTime(6))));return rows;
    }

    public async Task<IReadOnlyList<MediaPermissionSnapshot>> ListMediaPermissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection=await _connections.OpenAsync(cancellationToken);const string sql="SELECT RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload,UpdatedAtUtc FROM dbo.MamRoleMediaPermission ORDER BY RoleName,MediaKind;";await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<MediaPermissionSnapshot>();while(await reader.ReadAsync(cancellationToken))rows.Add(new MediaPermissionSnapshot(reader.GetString(0),reader.GetString(1),reader.GetBoolean(2),reader.GetBoolean(3),reader.GetBoolean(4),reader.GetBoolean(5),reader.GetBoolean(6),Utc(reader.GetDateTime(7))));return rows;
    }

    public async Task<MediaPermissionSnapshot> UpsertMediaPermissionAsync(UpsertMediaPermissionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var role=Required(request.RoleName,"role_required","Role name is required.",100);var kind=NormalizeMediaKind(request.MediaKind);await using var connection=await _connections.OpenAsync(cancellationToken);const string sql="""
            MERGE dbo.MamRoleMediaPermission target USING(SELECT @Role RoleName,@Kind MediaKind) source ON target.RoleName=source.RoleName AND target.MediaKind=source.MediaKind
            WHEN MATCHED THEN UPDATE SET CanView=@View,CanUpload=@Upload,CanEdit=@Edit,CanProcess=@Process,CanDownload=@Download,UpdatedAtUtc=@Now
            WHEN NOT MATCHED THEN INSERT(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload,UpdatedAtUtc) VALUES(@Role,@Kind,@View,@Upload,@Edit,@Process,@Download,@Now);
            """;await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Role",role);command.Parameters.AddWithValue("@Kind",kind);command.Parameters.AddWithValue("@View",request.CanView);command.Parameters.AddWithValue("@Upload",request.CanUpload);command.Parameters.AddWithValue("@Edit",request.CanEdit);command.Parameters.AddWithValue("@Process",request.CanProcess);command.Parameters.AddWithValue("@Download",request.CanDownload);command.Parameters.AddWithValue("@Now",DateTime.UtcNow);await command.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"media.permission.updated",Guid.Empty,$"{role}:{kind}",cancellationToken);return (await ListMediaPermissionsAsync(cancellationToken)).Single(x=>x.RoleName==role&&x.MediaKind==kind);
    }

    public async Task<bool> IsMediaActionAllowedAsync(IEnumerable<string> roles, string mediaKind, string action, CancellationToken cancellationToken = default)
    {
        var roleList=roles.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();if(roleList.Length==0)return false;mediaKind=NormalizeMediaKind(mediaKind);action=action.Trim().ToLowerInvariant();var column=action switch{"view"=>"CanView","upload"=>"CanUpload","edit"=>"CanEdit","process"=>"CanProcess","download"=>"CanDownload",_=>throw Error("unknown_media_action","Unknown media permission action.")};
        await using var connection=await _connections.OpenAsync(cancellationToken);var roleParams=string.Join(',',roleList.Select((_,i)=>$"@Role{i}"));var sql=$"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.MamRoleMediaPermission WHERE MediaKind=@Kind AND RoleName IN ({roleParams}) AND {column}=1) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Kind",mediaKind);for(var i=0;i<roleList.Length;i++)command.Parameters.AddWithValue($"@Role{i}",roleList[i]);return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture);
    }

    public async Task<string> GetAssetMediaKindAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection=await _connections.OpenAsync(cancellationToken);const string sql="SELECT TOP(1) tm.MediaType,o.OriginalFileName FROM dbo.MediaAsset a LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId LEFT JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId WHERE a.AssetId=@AssetId;";await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@AssetId",assetId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))throw Error("asset_not_found","Asset was not found.",404);if(!reader.IsDBNull(0)&&reader.GetString(0) is {Length:>0} media)return NormalizeMediaKind(media);return MediaKinds.FromFileName(reader.IsDBNull(1)?null:reader.GetString(1));
    }

    public async Task<DiscoveryDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await using var connection=await _connections.OpenAsync(cancellationToken);const string sql="""
            SELECT
              (SELECT COUNT_BIG(*) FROM dbo.MamCategory WHERE IsSystem=0),
              (SELECT COUNT_BIG(*) FROM dbo.MamAssetCategory WHERE CategoryId=@Uncategorized)+(SELECT COUNT_BIG(*) FROM dbo.MediaAsset a WHERE NOT EXISTS(SELECT 1 FROM dbo.MamAssetCategory ac WHERE ac.AssetId=a.AssetId)),
              (SELECT COUNT_BIG(DISTINCT AssetId) FROM dbo.MamAssetSearchContent),
              (SELECT COUNT_BIG(*) FROM dbo.MamAssetSearchContent WHERE SourceKind=N'transcript'),
              (SELECT COUNT_BIG(*) FROM dbo.MamAssetSearchContent WHERE SourceKind=N'ocr'),
              (SELECT COUNT_BIG(*) FROM dbo.MamReferenceSubject WHERE IsActive=1);
            """;await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Uncategorized",UncategorizedCategoryId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);await reader.ReadAsync(cancellationToken);return new DiscoveryDashboardSnapshot(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetInt64(5));
    }

    private async Task<CategorySnapshot> GetCategoryAsync(Guid categoryId,CancellationToken cancellationToken)
    {
        await using var connection=await _connections.OpenAsync(cancellationToken);const string sql="SELECT c.CategoryId,c.ParentCategoryId,c.NameEn,c.NameAr,c.IsSystem,c.SortOrder,c.Version,(SELECT COUNT_BIG(*) FROM dbo.MamAssetCategory ac WHERE ac.CategoryId=c.CategoryId),(SELECT COUNT_BIG(*) FROM dbo.MamCategory child WHERE child.ParentCategoryId=c.CategoryId),c.CreatedAtUtc,c.UpdatedAtUtc FROM dbo.MamCategory c WHERE c.CategoryId=@Id;";await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Id",categoryId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))throw Error("category_not_found","Category was not found.",404);return ReadCategory(reader);
    }

    private async Task ValidateParentAsync(Guid? parentId,Guid? categoryId,CancellationToken cancellationToken)
    {
        if(parentId is null)return;
        if(parentId==categoryId)throw Error("category_cycle","A category cannot be its own parent.",409);
        _=await GetCategoryAsync(parentId.Value,cancellationToken);
        if(categoryId is null)return;
        await using var connection=await _connections.OpenAsync(cancellationToken);
        const string sql="""
            WITH ancestors AS
            (
                SELECT CategoryId,ParentCategoryId FROM dbo.MamCategory WHERE CategoryId=@Parent
                UNION ALL
                SELECT p.CategoryId,p.ParentCategoryId
                FROM dbo.MamCategory p
                JOIN ancestors a ON p.CategoryId=a.ParentCategoryId
            )
            SELECT CASE WHEN EXISTS(SELECT 1 FROM ancestors WHERE CategoryId=@CategoryId) THEN 1 ELSE 0 END;
            """;
        await using var command=new SqlCommand(sql,connection){CommandTimeout=_connections.CommandTimeoutSeconds};
        command.Parameters.AddWithValue("@Parent",parentId.Value);
        command.Parameters.AddWithValue("@CategoryId",categoryId.Value);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture)==1)
            throw Error("category_cycle","Category hierarchy cannot contain cycles.",409);
    }

    private async Task EnsureAssetAsync(Guid assetId,CancellationToken cancellationToken)
    {await using var connection=await _connections.OpenAsync(cancellationToken);await using var command=new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MediaAsset WHERE AssetId=@Id;",connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Id",assetId);if(Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture)!=1)throw Error("asset_not_found","Asset was not found.",404);}

    private async Task<IReadOnlyList<Guid>> ListReferenceImageIdsAsync(Guid subjectId,CancellationToken cancellationToken)
    {await using var connection=await _connections.OpenAsync(cancellationToken);await using var command=new SqlCommand("SELECT AssetId FROM dbo.MamReferenceImage WHERE SubjectId=@Id ORDER BY AddedAtUtc,AssetId;",connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Id",subjectId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<Guid>();while(await reader.ReadAsync(cancellationToken))rows.Add(reader.GetGuid(0));return rows;}

    private async Task ReindexReferenceTagsAsync(Guid assetId,CancellationToken cancellationToken)
    {var tags=await ListAssetReferenceTagsAsync(assetId,cancellationToken);var text=string.Join(' ',tags.SelectMany(t=>new[]{t.NameEn,t.NameAr??string.Empty}));await UpsertTextAsync(assetId,DiscoverySources.Reference,null,text,null,null,cancellationToken);}

    private ValueTask AuditAsync(string actor,string action,Guid id,string? detail,CancellationToken cancellationToken)=>_audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,SafeActor(actor),action,"Discovery",id==Guid.Empty?"global":id.ToString("D"),"Success",detail),cancellationToken);
    private static CategorySnapshot ReadCategory(SqlDataReader r)=>new(r.GetGuid(0),r.IsDBNull(1)?null:r.GetGuid(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetBoolean(4),r.GetInt32(5),r.GetInt64(6),checked((int)r.GetInt64(7)),checked((int)r.GetInt64(8)),Utc(r.GetDateTime(9)),Utc(r.GetDateTime(10)));
    private static DateTimeOffset Utc(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));
    private static string SafeActor(string? actor)=>string.IsNullOrWhiteSpace(actor)?"unknown":actor.Trim()[..Math.Min(actor.Trim().Length,200)];
    private static string Required(string? value,string code,string message,int max){var v=value?.Trim();if(string.IsNullOrWhiteSpace(v))throw Error(code,message);return v[..Math.Min(v.Length,max)];}
    private static string? Optional(string? value,int max){var v=value?.Trim();return string.IsNullOrWhiteSpace(v)?null:v[..Math.Min(v.Length,max)];}
    private static string? JoinTags(IEnumerable<string>? tags){if(tags is null)return null;var cleaned=tags.Select(x=>x?.Trim()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).Select(x=>x!);var text=string.Join(';',cleaned);return text.Length==0?null:text[..Math.Min(text.Length,1000)];}
    private static IReadOnlyList<string> SplitTags(string? tags)=>string.IsNullOrWhiteSpace(tags)?Array.Empty<string>():tags.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string NormalizeMediaKind(string? kind)=>kind?.Trim().ToLowerInvariant() switch{"video"=>MediaKinds.Video,"audio"=>MediaKinds.Audio,"image"=>MediaKinds.Image,"document"=>MediaKinds.Document,"other"=>MediaKinds.Other,_=>MediaKinds.Other};
    private static DiscoveryRequestException Error(string code,string message,int status=400)=>new(code,message,status);

    private static void AddSearchParameters(SqlCommand command,string query,IReadOnlyList<string> tokens,DiscoverySearchRequest request,Guid uncategorized)
    {command.Parameters.AddWithValue("@RawQuery",query);command.Parameters.AddWithValue("@Uncategorized",uncategorized);for(var i=0;i<tokens.Count;i++)command.Parameters.AddWithValue($"@Token{i}",tokens[i]);if(request.CategoryId is not null)command.Parameters.AddWithValue("@CategoryId",request.CategoryId.Value);if(!string.IsNullOrWhiteSpace(request.MediaKind))command.Parameters.AddWithValue("@MediaKind",NormalizeMediaKind(request.MediaKind));}
}
