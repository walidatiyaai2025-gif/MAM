using System.Data;
using System.Security.Claims;
using System.Text;
using MAM.Application.Discovery;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P12UxEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var api = app.MapGroup($"{configuredApiBasePath}/v1/discovery");

        api.MapGet("/lookups/assets", async (
            string? query,
            string? mediaKind,
            int? limit,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, 100);
            var normalizedQuery = (query ?? string.Empty).Trim();
            await using var connection = await connections.OpenAsync(cancellationToken);
            const string sql = """
                SELECT TOP (300)
                       a.AssetId,a.Title,a.Lifecycle,a.Version,
                       tm.MediaType,o.OriginalFileName,a.UpdatedAtUtc
                FROM dbo.MediaAsset a
                LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
                LEFT JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId
                WHERE @Query=N''
                   OR a.Title LIKE N'%' + @Query + N'%'
                   OR CONVERT(nvarchar(36),a.AssetId) LIKE N'%' + @Query + N'%'
                   OR o.OriginalFileName LIKE N'%' + @Query + N'%'
                ORDER BY a.UpdatedAtUtc DESC,a.AssetId;
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
            command.Parameters.Add("@Query", SqlDbType.NVarChar, 300).Value = normalizedQuery;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<AssetLookupItem>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var resolvedKind = reader.IsDBNull(4)
                    ? MediaKinds.FromFileName(reader.IsDBNull(5) ? null : reader.GetString(5))
                    : reader.GetString(4);
                if (!string.IsNullOrWhiteSpace(mediaKind) && !string.Equals(resolvedKind, mediaKind.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                rows.Add(new AssetLookupItem(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    resolvedKind,
                    reader.GetByte(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc))));
                if (rows.Count >= take) break;
            }
            return Results.Ok(rows);
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/assets/{assetId:guid}/transcript-revisions", async (
            Guid assetId,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
            try
            {
                await using var connection = await connections.OpenAsync(cancellationToken);
                const string sql = """
                    SELECT RevisionId,RevisionNumber,SourceKind,Language,IsFinal,IsReady,Note,CreatedBy,CreatedAtUtc
                    FROM dbo.MamTranscriptRevision
                    WHERE AssetId=@AssetId
                    ORDER BY RevisionNumber DESC;
                    """;
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
                command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var rows = new List<TranscriptRevisionSummary>();
                while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSummary(assetId, reader));
                return Results.Ok(rows);
            }
            catch (SqlException ex)
            {
                return SqlFailure(ex, "transcript_revision_list_failed", "Transcript revision history could not be loaded.");
            }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/assets/{assetId:guid}/transcript-revisions/{revisionId:guid}", async (
            Guid assetId,
            Guid revisionId,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
            try
            {
                await using var connection = await connections.OpenAsync(cancellationToken);
                var revision = await FindRevisionAsync(connection, connections.CommandTimeoutSeconds, assetId, revisionId, cancellationToken);
                if (revision is null) return Results.NotFound(new { error = "transcript_revision_not_found", detail = "Transcript revision was not found." });
                var text = await discovery.GetTextAsync(assetId, revision.SourceKind, cancellationToken);
                if (text is null) return Results.Json(new { error = "transcript_revision_content_missing", detail = "The revision metadata exists but its indexed text is unavailable." }, statusCode: StatusCodes.Status409Conflict);
                return Results.Ok(new TranscriptRevisionSnapshot(revision, text.Text, text.Segments));
            }
            catch (SqlException ex)
            {
                return SqlFailure(ex, "transcript_revision_load_failed", "Transcript revision could not be loaded.");
            }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/assets/{assetId:guid}/transcript-revisions", async (
            Guid assetId,
            SaveTranscriptRevisionRequest request,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            if (!await Allowed(discovery, principal, assetId, "edit", cancellationToken)) return Results.Forbid();
            var correlationId = Guid.NewGuid().ToString("N");
            try
            {
                var original = await discovery.GetTextAsync(assetId, DiscoverySources.Transcript, cancellationToken);
                if (original is null)
                    return Results.Conflict(new { error = "transcript_not_available", detail = "Run speech transcription successfully before creating a manual revision.", correlationId });

                var segments = ValidateSegments(request.Segments);
                if (segments.Count == 0)
                    return Results.BadRequest(new { error = "transcript_revision_empty", detail = "At least one transcript segment with text is required.", correlationId });

                var revisionId = Guid.NewGuid();
                var sourceKind = $"transcript-rev-{revisionId:N}"[..31];
                var actor = Actor(principal);
                var language = Clean(request.Language, 20) ?? original.Language;
                var note = Clean(request.Note, 500);
                var fullText = string.Join(Environment.NewLine, segments.Select(x => x.Text));

                await using (var connection = await connections.OpenAsync(cancellationToken))
                {
                    const string insertSql = """
                        INSERT dbo.MamTranscriptRevision(RevisionId,AssetId,SourceKind,Language,IsFinal,IsReady,Note,CreatedBy)
                        VALUES(@RevisionId,@AssetId,@SourceKind,@Language,0,0,@Note,@CreatedBy);
                        """;
                    await using var insert = new SqlCommand(insertSql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
                    insert.Parameters.Add("@RevisionId", SqlDbType.UniqueIdentifier).Value = revisionId;
                    insert.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
                    insert.Parameters.Add("@SourceKind", SqlDbType.NVarChar, 40).Value = sourceKind;
                    insert.Parameters.Add("@Language", SqlDbType.NVarChar, 20).Value = (object?)language ?? DBNull.Value;
                    insert.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)note ?? DBNull.Value;
                    insert.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = actor;
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }

                try
                {
                    await discovery.UpsertTextAsync(assetId, sourceKind, language, fullText, null, segments, cancellationToken);
                    await using var connection = await connections.OpenAsync(cancellationToken);
                    await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
                    if (request.IsFinal)
                    {
                        await using var clear = new SqlCommand("UPDATE dbo.MamTranscriptRevision SET IsFinal=0 WHERE AssetId=@AssetId AND IsFinal=1;", connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds };
                        clear.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
                        await clear.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await using (var ready = new SqlCommand("UPDATE dbo.MamTranscriptRevision SET IsReady=1,IsFinal=@IsFinal WHERE RevisionId=@RevisionId AND AssetId=@AssetId;", connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                    {
                        ready.Parameters.Add("@IsFinal", SqlDbType.Bit).Value = request.IsFinal;
                        ready.Parameters.Add("@RevisionId", SqlDbType.UniqueIdentifier).Value = revisionId;
                        ready.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
                        await ready.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    try
                    {
                        await using var cleanupConnection = await connections.OpenAsync(CancellationToken.None);
                        await using var cleanup = new SqlCommand("DELETE dbo.MamTranscriptRevision WHERE RevisionId=@RevisionId AND IsReady=0;", cleanupConnection) { CommandTimeout = connections.CommandTimeoutSeconds };
                        cleanup.Parameters.Add("@RevisionId", SqlDbType.UniqueIdentifier).Value = revisionId;
                        await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                    catch { }
                    throw;
                }

                await using var readConnection = await connections.OpenAsync(cancellationToken);
                var saved = await FindRevisionAsync(readConnection, connections.CommandTimeoutSeconds, assetId, revisionId, cancellationToken)
                            ?? throw new InvalidOperationException("Saved transcript revision could not be reloaded.");
                return Results.Ok(new TranscriptRevisionSnapshot(saved, fullText, segments));
            }
            catch (TranscriptRevisionValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Code, detail = ex.Message, correlationId });
            }
            catch (SqlException ex)
            {
                return Results.Json(new { error = "transcript_revision_database_error", detail = "The transcript revision could not be saved because the database rejected the operation.", technicalDetail = ex.Message, sqlErrorNumber = ex.Number, correlationId }, statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = "transcript_revision_save_failed", detail = "The transcript revision could not be saved.", technicalDetail = ex.Message, correlationId }, statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);
    }

    private static async Task<bool> Allowed(IDiscoveryService discovery, ClaimsPrincipal principal, Guid assetId, string action, CancellationToken cancellationToken)
    {
        var mediaKind = await discovery.GetAssetMediaKindAsync(assetId, cancellationToken);
        return await discovery.IsMediaActionAllowedAsync(Roles(principal), mediaKind, action, cancellationToken);
    }

    private static string[] Roles(ClaimsPrincipal principal) => principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Actor(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";

    private static List<TextSegmentSnapshot> ValidateSegments(IReadOnlyList<TranscriptSegmentEdit>? input)
    {
        if (input is null || input.Count == 0) return [];
        if (input.Count > 10000) throw new TranscriptRevisionValidationException("transcript_revision_too_many_segments", "A transcript revision cannot contain more than 10,000 segments.");
        var output = new List<TextSegmentSnapshot>();
        foreach (var item in input.OrderBy(x => x.SegmentIndex))
        {
            var value = (item.Text ?? string.Empty).Trim();
            if (value.Length == 0) continue;
            if (value.Length > 20000) throw new TranscriptRevisionValidationException("transcript_segment_too_long", "A transcript segment cannot exceed 20,000 characters.");
            if (item.StartMs is < 0 || item.EndMs is < 0 || (item.StartMs is not null && item.EndMs is not null && item.EndMs < item.StartMs))
                throw new TranscriptRevisionValidationException("transcript_segment_time_invalid", "Transcript segment timestamps are invalid.");
            output.Add(new TextSegmentSnapshot(output.Count, item.StartMs, item.EndMs, null, value));
        }
        return output;
    }

    private static async Task<TranscriptRevisionSummary?> FindRevisionAsync(SqlConnection connection, int timeoutSeconds, Guid assetId, Guid revisionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT RevisionId,RevisionNumber,SourceKind,Language,IsFinal,IsReady,Note,CreatedBy,CreatedAtUtc
            FROM dbo.MamTranscriptRevision
            WHERE AssetId=@AssetId AND RevisionId=@RevisionId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        command.Parameters.Add("@RevisionId", SqlDbType.UniqueIdentifier).Value = revisionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSummary(assetId, reader) : null;
    }

    private static TranscriptRevisionSummary ReadSummary(Guid assetId, SqlDataReader reader) => new(
        reader.GetGuid(0),
        assetId,
        reader.GetInt64(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetString(7),
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)));

    private static string? Clean(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static IResult SqlFailure(SqlException ex, string code, string detail) =>
        Results.Json(new { error = code, detail, technicalDetail = ex.Message, sqlErrorNumber = ex.Number }, statusCode: StatusCodes.Status500InternalServerError);

    public sealed record AssetLookupItem(Guid AssetId, string Title, string MediaKind, byte Lifecycle, long Version, string? OriginalFileName, DateTimeOffset UpdatedAtUtc);
    public sealed record TranscriptSegmentEdit(int SegmentIndex, long? StartMs, long? EndMs, string Text);
    public sealed record SaveTranscriptRevisionRequest(string? Language, bool IsFinal, string? Note, IReadOnlyList<TranscriptSegmentEdit>? Segments);
    public sealed record TranscriptRevisionSummary(Guid RevisionId, Guid AssetId, long RevisionNumber, string SourceKind, string? Language, bool IsFinal, bool IsReady, string? Note, string CreatedBy, DateTimeOffset CreatedAtUtc);
    public sealed record TranscriptRevisionSnapshot(TranscriptRevisionSummary Revision, string Text, IReadOnlyList<TextSegmentSnapshot> Segments);

    private sealed class TranscriptRevisionValidationException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
