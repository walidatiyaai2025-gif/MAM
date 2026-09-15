using System.Data;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P127ProcessingEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var processing = app.MapGroup($"{configuredApiBasePath}/v1/processing");

        processing.MapGet("/jobs/page", async (
            int? page,
            int? pageSize,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 10, 1, 50);
            var offset = checked((currentPage - 1) * size);
            await using var connection = await connections.OpenAsync(cancellationToken);

            long total;
            await using (var count = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MamProcessingJob;", connection)
                         { CommandTimeout = connections.CommandTimeoutSeconds })
                total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));

            const string sql = """
                SELECT j.JobId,j.AssetId,a.Title,j.ProfileId,j.ProfileVersion,j.State,j.AttemptCount,j.LastError,
                       j.CreatedAtUtc,j.UpdatedAtUtc,j.CompletedAtUtc,
                       COALESCE(tm.MediaType,N'Other') MediaType,
                       es.State ExtractionState,es.ProgressPercent,es.Detail,es.StartedAtUtc,es.CompletedAtUtc ExtractionCompletedAtUtc
                FROM dbo.MamProcessingJob j
                JOIN dbo.MediaAsset a ON a.AssetId=j.AssetId
                LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=j.AssetId
                LEFT JOIN dbo.MamTextExtractionStatus es
                  ON es.AssetId=j.AssetId
                 AND es.ExtractionKind=CASE
                       WHEN j.ProfileId=N'transcript-text-v1' THEN N'transcript'
                       WHEN j.ProfileId=N'ocr-text-v1' THEN N'ocr'
                       ELSE N'__none__'
                     END
                ORDER BY CASE j.State WHEN 1 THEN 0 WHEN 0 THEN 1 WHEN 3 THEN 2 ELSE 3 END,j.UpdatedAtUtc DESC,j.JobId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
            var rows = new List<object>();
            await using (var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds })
            {
                command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
                command.Parameters.Add("@PageSize", SqlDbType.Int).Value = size;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var started = reader.IsDBNull(15) ? (DateTimeOffset?)null : Utc(reader.GetDateTime(15));
                    var extractionCompleted = reader.IsDBNull(16) ? (DateTimeOffset?)null : Utc(reader.GetDateTime(16));
                    var elapsedMs = started is null
                        ? (long?)null
                        : Math.Max(0L, (long)((extractionCompleted ?? DateTimeOffset.UtcNow) - started.Value).TotalMilliseconds);
                    rows.Add(new
                    {
                        jobId = reader.GetGuid(0),
                        assetId = reader.GetGuid(1),
                        title = reader.GetString(2),
                        profileId = reader.GetString(3),
                        profileVersion = reader.GetInt32(4),
                        state = reader.GetByte(5),
                        attemptCount = reader.GetInt32(6),
                        lastError = reader.IsDBNull(7) ? null : reader.GetString(7),
                        createdAtUtc = Utc(reader.GetDateTime(8)),
                        updatedAtUtc = Utc(reader.GetDateTime(9)),
                        completedAtUtc = reader.IsDBNull(10) ? null : Utc(reader.GetDateTime(10)),
                        mediaType = reader.GetString(11),
                        extractionState = reader.IsDBNull(12) ? null : reader.GetString(12),
                        progressPercent = reader.IsDBNull(13) ? (int?)null : reader.GetByte(13),
                        extractionDetail = reader.IsDBNull(14) ? null : reader.GetString(14),
                        startedAtUtc = started,
                        extractionCompletedAtUtc = extractionCompleted,
                        elapsedMs
                    });
                }
            }

            return Results.Ok(new
            {
                items = rows,
                totalCount = total,
                page = currentPage,
                pageSize = size,
                totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size)),
                generatedAtUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}