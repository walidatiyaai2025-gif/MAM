using System.Data;
using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P12AssetDeletionEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var logger = app.Logger;
        var admin = app.MapGroup($"{configuredApiBasePath}/v1/admin");

        admin.MapDelete("/assets/{assetId:guid}", async (
            Guid assetId,
            ClaimsPrincipal principal,
            SqlServerConnectionFactory connections,
            MamSettings settings,
            IAuditSink audit,
            CancellationToken cancellationToken) =>
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var actor = principal.FindFirstValue(ClaimTypes.WindowsAccountName)
                        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? principal.Identity?.Name
                        ?? "unknown";

            try
            {
                if (!IsFileSystem(settings.Storage.Primary.Type) || !IsFileSystem(settings.Storage.Backup.Type))
                {
                    return Results.Conflict(new
                    {
                        error = "asset_delete_storage_provider_unsupported",
                        detail = "Permanent asset deletion currently requires FileSystem/Mock Primary and Backup Storage so media can be staged safely before database deletion.",
                        correlationId
                    });
                }

                await using var connection = await connections.OpenAsync(cancellationToken);
                var snapshot = await ReadSnapshotAsync(connection, assetId, cancellationToken);
                if (snapshot is null)
                {
                    return Results.NotFound(new
                    {
                        error = "asset_not_found",
                        detail = $"Asset {assetId:D} was not found in the authoritative catalog.",
                        correlationId
                    });
                }

                if (snapshot.ActiveProcessingJobs > 0 || snapshot.ActiveBackupJobs > 0)
                {
                    return Results.Conflict(new
                    {
                        error = "asset_busy",
                        detail = $"Asset cannot be deleted while work is active. Processing jobs: {snapshot.ActiveProcessingJobs}; backup jobs: {snapshot.ActiveBackupJobs}. Wait for the jobs to finish and retry.",
                        correlationId,
                        activeProcessingJobs = snapshot.ActiveProcessingJobs,
                        activeBackupJobs = snapshot.ActiveBackupJobs
                    });
                }

                var storageObjects = snapshot.PrimaryKeys
                    .Select(key => new StorageObject(settings.Storage.Primary.Root, key))
                    .Concat(snapshot.BackupKeys.Select(key => new StorageObject(settings.Storage.Backup.Root, key)))
                    .DistinctBy(item => $"{item.Root}\n{item.ObjectKey}", StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var deletionId = Guid.NewGuid();
                var staged = new List<StagedObject>();
                var missingObjects = 0;

                try
                {
                    foreach (var item in storageObjects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var stagedItem = StageObject(item, deletionId);
                        if (stagedItem is null) missingObjects++;
                        else staged.Add(stagedItem);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    RestoreStaged(staged, logger, correlationId);
                    logger.LogWarning(ex, "Asset deletion storage staging failed for {AssetId}; correlation {CorrelationId}.", assetId, correlationId);
                    return Results.Json(new
                    {
                        error = "asset_storage_busy",
                        detail = "One or more media files could not be staged for deletion. A preview, antivirus scanner, backup process, or another process may still have the file open. Close active access and retry.",
                        technicalDetail = ex.Message,
                        correlationId
                    }, statusCode: StatusCodes.Status409Conflict);
                }

                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                try
                {
                    const string deleteSql = """
                        DELETE dbo.MamBackupJob WHERE AssetId=@AssetId;
                        DELETE dbo.MamBackupProtection WHERE AssetId=@AssetId;

                        DELETE dbo.MamAssetReferenceTag WHERE AssetId=@AssetId;
                        DELETE dbo.MamReferenceImage WHERE AssetId=@AssetId;
                        DELETE dbo.MamTextExtractionStatus WHERE AssetId=@AssetId;
                        DELETE dbo.MamAssetTextSegment WHERE AssetId=@AssetId;
                        DELETE dbo.MamAssetSearchContent WHERE AssetId=@AssetId;
                        DELETE dbo.MamAssetCategory WHERE AssetId=@AssetId;

                        DELETE dbo.MamCollectionAsset WHERE AssetId=@AssetId;
                        DELETE dbo.MamAssetTag WHERE AssetId=@AssetId;
                        DELETE dbo.MamAssetMetadata WHERE AssetId=@AssetId;

                        DELETE dbo.MamMediaDerivative WHERE AssetId=@AssetId;
                        DELETE dbo.MamProcessingJob WHERE AssetId=@AssetId;
                        DELETE dbo.MamTechnicalMetadata WHERE AssetId=@AssetId;

                        -- Bulk-import history is retained, but its nullable references must be
                        -- detached before deleting the authoritative upload session / asset rows.
                        UPDATE bulkItem
                        SET UploadSessionId=NULL,
                            AssetId=NULL
                        FROM dbo.MamBulkImportItem bulkItem
                        LEFT JOIN dbo.MamUploadSession uploadSession
                          ON uploadSession.SessionId=bulkItem.UploadSessionId
                        WHERE bulkItem.AssetId=@AssetId
                           OR uploadSession.AssetId=@AssetId;

                        DELETE receipt
                        FROM dbo.MamUploadChunkReceipt receipt
                        INNER JOIN dbo.MamUploadSession session ON session.SessionId=receipt.SessionId
                        WHERE session.AssetId=@AssetId;
                        DELETE dbo.MamUploadSession WHERE AssetId=@AssetId;

                        DELETE dbo.MamMediaOriginal WHERE AssetId=@AssetId;
                        DELETE dbo.MediaAsset WHERE AssetId=@AssetId;
                        """;
                    await using var command = new SqlCommand(deleteSql, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds };
                    command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                    RestoreStaged(staged, logger, correlationId);
                    throw;
                }

                var purgeFailures = PurgeStaged(staged, logger, correlationId);
                var outcome = purgeFailures == 0 ? "Success" : "Partial";
                var auditDetail = $"title={snapshot.Title};database=deleted;staged={staged.Count};missing={missingObjects};purgeFailures={purgeFailures}";
                await audit.AppendAsync(new AuditEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    actor,
                    "catalog.asset.permanently-deleted",
                    "MediaAsset",
                    assetId.ToString("D"),
                    outcome,
                    auditDetail), cancellationToken);

                if (purgeFailures > 0)
                {
                    return Results.Json(new
                    {
                        error = "asset_deleted_storage_purge_pending",
                        detail = "The asset and all database records were deleted, but one or more already-staged file remnants could not be purged immediately. They are no longer reachable through MAM and require storage cleanup.",
                        correlationId,
                        assetId,
                        databaseDeleted = true,
                        stagedObjects = staged.Count,
                        missingObjects,
                        purgeFailures
                    }, statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Ok(new
                {
                    deleted = true,
                    assetId,
                    title = snapshot.Title,
                    databaseRecordsDeleted = true,
                    storageObjectsDeleted = staged.Count,
                    missingStorageObjects = missingObjects,
                    auditRetained = true,
                    correlationId,
                    detail = "Asset, derivatives including visual segment thumbnails, OCR/transcript/index data, categories/tags/collection links, processing/protection records, upload records, Primary media and Backup copy were deleted. Bulk-import history was retained with deleted asset/upload references detached. The immutable audit event was retained."
                });
            }
            catch (SqlException ex)
            {
                logger.LogError(ex, "Asset deletion SQL failure for {AssetId}; correlation {CorrelationId}.", assetId, correlationId);
                return Results.Json(new
                {
                    error = "asset_delete_database_error",
                    detail = "The authoritative database rejected the delete operation. No successful deletion was recorded.",
                    sqlErrorNumber = ex.Number,
                    correlationId
                }, statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Results.Json(new
                {
                    error = "asset_delete_cancelled",
                    detail = "Asset deletion was cancelled before completion.",
                    correlationId
                }, statusCode: 499);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected asset deletion failure for {AssetId}; correlation {CorrelationId}.", assetId, correlationId);
                return Results.Json(new
                {
                    error = "asset_delete_failed",
                    detail = $"Asset deletion failed with {ex.GetType().Name}: {ex.Message}",
                    correlationId
                }, statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private static async Task<DeleteSnapshot?> ReadSnapshotAsync(SqlConnection connection, Guid assetId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Title FROM dbo.MediaAsset WHERE AssetId=@AssetId;
            SELECT COUNT(*) FROM dbo.MamProcessingJob WHERE AssetId=@AssetId AND State IN (0,1);
            SELECT COUNT(*) FROM dbo.MamBackupJob WHERE AssetId=@AssetId AND State IN (0,1);
            SELECT ObjectKey FROM dbo.MamMediaOriginal WHERE AssetId=@AssetId;
            SELECT ObjectKey FROM dbo.MamMediaDerivative WHERE AssetId=@AssetId;
            SELECT ThumbnailObjectKey FROM dbo.MamVisualSegment WHERE AssetId=@AssetId AND ThumbnailObjectKey IS NOT NULL;
            SELECT BackupObjectKey FROM dbo.MamBackupProtection WHERE AssetId=@AssetId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = assetId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;
        var title = reader.GetString(0);

        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var processing = reader.GetInt32(0);

        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var backup = reader.GetInt32(0);

        var primaryKeys = new List<string>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) primaryKeys.Add(reader.GetString(0));
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) primaryKeys.Add(reader.GetString(0));
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) primaryKeys.Add(reader.GetString(0));

        var backupKeys = new List<string>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) backupKeys.Add(reader.GetString(0));

        return new DeleteSnapshot(title, processing, backup, primaryKeys, backupKeys);
    }

    private static bool IsFileSystem(string? type) =>
        string.Equals(type, "FileSystem", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Mock", StringComparison.OrdinalIgnoreCase);

    private static StagedObject? StageObject(StorageObject item, Guid deletionId)
    {
        var rootPath = ResolveConfiguredRoot(item.Root);
        var source = ResolveObjectPath(rootPath, item.ObjectKey);
        if (!File.Exists(source)) return null;

        var normalizedKey = NormalizeObjectKey(item.ObjectKey);
        var stagingRoot = Path.Combine(rootPath, ".mam-delete-staging", deletionId.ToString("N"));
        var stagedPath = Path.GetFullPath(Path.Combine(stagingRoot, normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var stagingRootWithSeparator = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!stagedPath.StartsWith(stagingRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Storage staging path escaped the configured storage root.");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        File.Move(source, stagedPath, overwrite: false);
        return new StagedObject(source, stagedPath);
    }

    private static int PurgeStaged(IEnumerable<StagedObject> staged, ILogger logger, string correlationId)
    {
        var failures = 0;
        foreach (var item in staged)
        {
            try
            {
                if (File.Exists(item.StagedPath)) File.Delete(item.StagedPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures++;
                logger.LogError(ex, "Failed to purge staged deleted object {Path}; correlation {CorrelationId}.", item.StagedPath, correlationId);
            }
        }
        return failures;
    }

    private static void RestoreStaged(IEnumerable<StagedObject> staged, ILogger logger, string correlationId)
    {
        foreach (var item in staged.Reverse())
        {
            try
            {
                if (!File.Exists(item.StagedPath)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
                File.Move(item.StagedPath, item.OriginalPath, overwrite: false);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Failed to restore staged asset media {Path}; correlation {CorrelationId}.", item.OriginalPath, correlationId);
            }
        }
    }

    private static string ResolveObjectPath(string resolvedRoot, string objectKey)
    {
        var normalized = NormalizeObjectKey(objectKey);
        var combined = Path.GetFullPath(Path.Combine(resolvedRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Storage object key escaped its configured storage root.");
        return combined;
    }

    private static string ResolveConfiguredRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidDataException("Storage root is empty.");

        var configured = root.Trim();
        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        var explicitBase = Environment.GetEnvironmentVariable("MAM_STORAGE_BASE_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitBase))
            return Path.GetFullPath(configured, Path.GetFullPath(explicitBase));

        var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var fullConfigPath = Path.GetFullPath(configPath);
            var configDirectory = Path.GetDirectoryName(fullConfigPath);
            if (!string.IsNullOrWhiteSpace(configDirectory))
                return Path.GetFullPath(configured, configDirectory);
        }

        return Path.GetFullPath(configured);
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new InvalidDataException("Storage object key is empty.");
        var raw = objectKey.Trim().Replace('\\', '/').Trim('/');
        if (raw.Length == 0 || raw.StartsWith("/", StringComparison.Ordinal) || raw.Contains(':') || Path.IsPathRooted(raw))
            throw new InvalidDataException("Storage object key is not a safe relative path.");
        var segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Storage object key contains an unsafe path segment.");
        return string.Join('/', segments);
    }

    private sealed record DeleteSnapshot(
        string Title,
        int ActiveProcessingJobs,
        int ActiveBackupJobs,
        IReadOnlyList<string> PrimaryKeys,
        IReadOnlyList<string> BackupKeys);

    private sealed record StorageObject(string Root, string ObjectKey);
    private sealed record StagedObject(string OriginalPath, string StagedPath);
}
