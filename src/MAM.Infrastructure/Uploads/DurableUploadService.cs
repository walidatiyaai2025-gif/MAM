using System.Security.Cryptography;
using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Storage;
using MAM.Application.Uploads;
using MAM.Domain.Assets;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Uploads;

public sealed class DurableUploadService : IDurableUploadService
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAssetCatalog _catalog;
    private readonly IAuditSink _audit;
    private readonly MamSettings _settings;
    private readonly ServerUploadStagingStore _staging;

    public DurableUploadService(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IAssetCatalog catalog,
        IAuditSink audit,
        MamSettings settings)
    {
        _connections = connections;
        _primary = primary;
        _catalog = catalog;
        _audit = audit;
        _settings = settings;
        _staging = new ServerUploadStagingStore(settings.Storage.Primary.Root);
    }

    public async ValueTask<UploadSessionSnapshot> CreateSessionAsync(
        CreateUploadSessionRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = NormalizeTitle(request.Title);
        var fileName = NormalizeFileName(request.OriginalFileName);
        var expectedSha = NormalizeSha(request.ExpectedSha256);
        if (request.ExpectedLength <= 0) throw Bad("invalid_length", "Expected file length must be greater than zero.");
        var maxBytes = checked(_settings.Upload.MaxFileSizeGB * 1024L * 1024L * 1024L);
        if (request.ExpectedLength > maxBytes) throw Bad("file_too_large", "The file exceeds the configured maximum upload size.", 413);

        var extension = Path.GetExtension(fileName);
        var allowed = _settings.Upload.AllowedExtensions.Any(value =>
            string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));
        if (!allowed && !_settings.Upload.QuarantineUnknownFiles)
            throw Bad("file_type_not_allowed", "The file extension is not allowed by upload policy.", 415);
        var quarantine = !allowed;

        var duplicate = await FindOriginalByShaAsync(expectedSha, cancellationToken);
        if (duplicate is not null)
            throw Bad("duplicate_detected", "An authoritative Primary original with the same SHA-256 already exists.", 409, duplicate.Value);

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var chunkSize = checked(_settings.Upload.ChunkSizeMB * 1024 * 1024);
        var expires = now.AddHours(_settings.Upload.SessionExpiryHours);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            INSERT dbo.MamUploadSession
            (SessionId, AssetId, Title, OriginalFileName, ExpectedLength, ExpectedSha256, ChunkSizeBytes,
             ReceivedLength, State, IsQuarantined, CreatedBy, CreatedAtUtc, UpdatedAtUtc, ExpiresAtUtc)
            VALUES
            (@SessionId, @AssetId, @Title, @OriginalFileName, @ExpectedLength, @ExpectedSha256, @ChunkSizeBytes,
             0, 0, @IsQuarantined, @CreatedBy, @Now, @Now, @ExpiresAtUtc);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@AssetId", assetId);
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue("@OriginalFileName", fileName);
        command.Parameters.AddWithValue("@ExpectedLength", request.ExpectedLength);
        command.Parameters.AddWithValue("@ExpectedSha256", expectedSha);
        command.Parameters.AddWithValue("@ChunkSizeBytes", chunkSize);
        command.Parameters.AddWithValue("@IsQuarantined", quarantine);
        command.Parameters.AddWithValue("@CreatedBy", NormalizeActor(actorId));
        command.Parameters.AddWithValue("@Now", now.UtcDateTime);
        command.Parameters.AddWithValue("@ExpiresAtUtc", expires.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await _audit.AppendAsync(NewAudit(actorId, "upload.session.created", sessionId, "Success",
            $"asset={assetId:D};length={request.ExpectedLength};quarantine={quarantine}"), cancellationToken);
        return new UploadSessionSnapshot(
            new UploadSessionContract(sessionId, assetId, fileName, request.ExpectedLength, expectedSha, chunkSize, expires),
            title, 0, UploadSessionState.Receiving, quarantine, null, null);
    }

    public async ValueTask<UploadSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSessionAsync(sessionId, cancellationToken)
            ?? throw Bad("session_not_found", "Upload session was not found.", 404);
        if (snapshot.State == UploadSessionState.Receiving && snapshot.Session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await SetStateAsync(sessionId, UploadSessionState.Expired, "Upload session expired before finalization.", null, cancellationToken);
            snapshot = snapshot with { State = UploadSessionState.Expired, Error = "Upload session expired before finalization." };
        }
        return snapshot;
    }

    public async ValueTask<UploadChunkResult> PutChunkAsync(
        Guid sessionId,
        long offset,
        string expectedChunkSha256,
        Stream body,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session.State == UploadSessionState.Expired) throw Bad("session_expired", "Upload session has expired.", 410);
        if (session.State != UploadSessionState.Receiving) throw Bad("session_not_receiving", "Upload session is not accepting chunks.", 409);
        if (offset != session.ReceivedLength)
            throw Bad("offset_conflict", $"Expected next offset {session.ReceivedLength}, received {offset}.", 409);

        await _staging.ReconcileLengthAsync(sessionId, session.ReceivedLength, cancellationToken);
        var chunkSha = NormalizeSha(expectedChunkSha256);
        var payload = await ReadChunkAsync(body, session.Session.ChunkSizeBytes, cancellationToken);
        if (payload.Length == 0) throw Bad("empty_chunk", "Upload chunks cannot be empty.");
        if (session.ReceivedLength + payload.Length > session.Session.ExpectedLength)
            throw Bad("chunk_exceeds_expected_length", "Chunk would exceed the declared file length.");

        var actualChunkSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (!HashesEqual(chunkSha, actualChunkSha))
            throw Bad("chunk_hash_mismatch", "Chunk SHA-256 did not match the supplied chunk digest.", 422);

        await _staging.AppendAsync(sessionId, session.ReceivedLength, payload, cancellationToken);
        var newLength = session.ReceivedLength + payload.Length;

        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string receiptSql = """
                INSERT dbo.MamUploadChunkReceipt(SessionId, Offset, Length, ChunkSha256)
                VALUES(@SessionId, @Offset, @Length, @ChunkSha256);
                """;
            await using (var receipt = new SqlCommand(receiptSql, connection, (SqlTransaction)transaction)
                         { CommandTimeout = _connections.CommandTimeoutSeconds })
            {
                receipt.Parameters.AddWithValue("@SessionId", sessionId);
                receipt.Parameters.AddWithValue("@Offset", offset);
                receipt.Parameters.AddWithValue("@Length", payload.Length);
                receipt.Parameters.AddWithValue("@ChunkSha256", actualChunkSha);
                await receipt.ExecuteNonQueryAsync(cancellationToken);
            }

            const string updateSql = """
                UPDATE dbo.MamUploadSession
                SET ReceivedLength = @NewLength, UpdatedAtUtc = @Now
                WHERE SessionId = @SessionId AND State = 0 AND ReceivedLength = @Offset;
                """;
            await using (var update = new SqlCommand(updateSql, connection, (SqlTransaction)transaction)
                         { CommandTimeout = _connections.CommandTimeoutSeconds })
            {
                update.Parameters.AddWithValue("@SessionId", sessionId);
                update.Parameters.AddWithValue("@NewLength", newLength);
                update.Parameters.AddWithValue("@Offset", offset);
                update.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw Bad("offset_conflict", "Upload session offset changed while the chunk was being committed.", 409);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await _staging.TruncateAsync(sessionId, session.ReceivedLength, CancellationToken.None);
            throw;
        }

        await _audit.AppendAsync(NewAudit(actorId, "upload.chunk.accepted", sessionId, "Success",
            $"offset={offset};length={payload.Length};received={newLength}"), cancellationToken);
        var receiptResult = new UploadChunkReceipt(offset, payload.Length, actualChunkSha);
        return new UploadChunkResult(sessionId, newLength, session.Session.ExpectedLength,
            newLength == session.Session.ExpectedLength, receiptResult);
    }

    public async ValueTask<UploadFinalizeResult> FinalizeAsync(
        Guid sessionId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session.State == UploadSessionState.Expired) throw Bad("session_expired", "Upload session has expired.", 410);
        if (session.State == UploadSessionState.Completed && session.PrimaryObjectKey is not null)
            return new UploadFinalizeResult(sessionId, session.Session.AssetId, session.PrimaryObjectKey,
                session.Session.ExpectedLength, session.Session.ExpectedSha256, UploadSessionState.Completed);
        if (session.State != UploadSessionState.Receiving)
            throw Bad("session_not_finalizable", "Upload session cannot be finalized from its current state.", 409);
        if (session.ReceivedLength != session.Session.ExpectedLength)
            throw Bad("upload_incomplete", $"Upload is incomplete: {session.ReceivedLength} of {session.Session.ExpectedLength} bytes received.", 409);

        await _staging.ReconcileLengthAsync(sessionId, session.ReceivedLength, cancellationToken);
        if (session.IsQuarantined)
        {
            await SetStateAsync(sessionId, UploadSessionState.Quarantined,
                "File extension is outside the allow-list and was quarantined instead of promoted.", null, cancellationToken);
            await _audit.AppendAsync(NewAudit(actorId, "upload.quarantined", sessionId, "Rejected", session.Session.OriginalFileName), cancellationToken);
            throw Bad("file_quarantined", "The uploaded file is quarantined by policy and cannot be promoted to Primary Storage.", 422);
        }

        var (actualLength, actualSha) = await _staging.MeasureAsync(sessionId, cancellationToken);
        if (actualLength != session.Session.ExpectedLength || !HashesEqual(actualSha, session.Session.ExpectedSha256))
        {
            await SetStateAsync(sessionId, UploadSessionState.Failed,
                "Final size or SHA-256 verification failed before Primary Storage promotion.", null, cancellationToken);
            await _audit.AppendAsync(NewAudit(actorId, "upload.integrity-failed", sessionId, "Rejected",
                $"expectedLength={session.Session.ExpectedLength};actualLength={actualLength}"), cancellationToken);
            throw Bad("integrity_mismatch", "Final server-side size/SHA-256 verification failed.", 422);
        }

        var duplicate = await FindOriginalByShaAsync(actualSha, cancellationToken);
        if (duplicate is not null)
        {
            await SetStateAsync(sessionId, UploadSessionState.Duplicate,
                "Duplicate SHA-256 matches an existing authoritative original.", null, cancellationToken);
            await _audit.AppendAsync(NewAudit(actorId, "upload.duplicate-detected", sessionId, "Conflict", duplicate.Value.ToString("D")), cancellationToken);
            throw Bad("duplicate_detected", "An authoritative Primary original with the same SHA-256 already exists.", 409, duplicate.Value);
        }

        var storageHealth = await _primary.GetHealthAsync(cancellationToken);
        if (!storageHealth.IsReady) throw Bad("primary_storage_degraded", storageHealth.Detail, 503);
        var objectKey = BuildObjectKey(session.Session.AssetId, session.Session.OriginalFileName, DateTimeOffset.UtcNow);
        StorageWriteResult write;
        await using (var source = await _staging.OpenReadAsync(sessionId, cancellationToken))
        {
            write = await _primary.WriteAsync(objectKey, source, actualSha, cancellationToken);
        }

        try
        {
            var verification = await _primary.VerifyAsync(objectKey, actualSha, cancellationToken);
            if (!verification.Exists || !verification.ChecksumMatches || verification.Length != session.Session.ExpectedLength)
                throw Bad("primary_verification_failed", "Primary Storage verification failed after write.", 503);

            var catalogResult = await _catalog.CreateWithIdAsync(new AssetId(session.Session.AssetId), session.Title, actorId, cancellationToken);
            if (catalogResult.Status != CatalogMutationStatus.Created)
                throw Bad("catalog_promotion_failed", catalogResult.Error ?? "Catalog asset could not be created after Primary verification.", 503);

            await using var connection = await _connections.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string originalSql = """
                INSERT dbo.MamMediaOriginal
                (AssetId, StorageTargetId, ObjectKey, OriginalFileName, Length, Sha256, VerifiedAtUtc)
                VALUES(@AssetId, @StorageTargetId, @ObjectKey, @OriginalFileName, @Length, @Sha256, @VerifiedAtUtc);
                """;
            await using (var original = new SqlCommand(originalSql, connection, (SqlTransaction)transaction)
                         { CommandTimeout = _connections.CommandTimeoutSeconds })
            {
                original.Parameters.AddWithValue("@AssetId", session.Session.AssetId);
                original.Parameters.AddWithValue("@StorageTargetId", _primary.TargetId);
                original.Parameters.AddWithValue("@ObjectKey", write.ObjectKey);
                original.Parameters.AddWithValue("@OriginalFileName", session.Session.OriginalFileName);
                original.Parameters.AddWithValue("@Length", write.Length);
                original.Parameters.AddWithValue("@Sha256", write.Sha256);
                original.Parameters.AddWithValue("@VerifiedAtUtc", DateTime.UtcNow);
                await original.ExecuteNonQueryAsync(cancellationToken);
            }

            const string completeSql = """
                UPDATE dbo.MamUploadSession
                SET State = 1, PrimaryObjectKey = @ObjectKey, Error = NULL, UpdatedAtUtc = @Now
                WHERE SessionId = @SessionId AND State = 0;
                """;
            await using (var complete = new SqlCommand(completeSql, connection, (SqlTransaction)transaction)
                         { CommandTimeout = _connections.CommandTimeoutSeconds })
            {
                complete.Parameters.AddWithValue("@SessionId", sessionId);
                complete.Parameters.AddWithValue("@ObjectKey", write.ObjectKey);
                complete.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw Bad("session_completion_conflict", "Upload session changed before completion could be committed.", 409);
            }
            await transaction.CommitAsync(cancellationToken);

            await _audit.AppendAsync(NewAudit(actorId, "upload.primary.committed", sessionId, "Success",
                $"asset={session.Session.AssetId:D};target={_primary.TargetId};length={write.Length};sha256={write.Sha256}"), cancellationToken);
            await _staging.DeleteAsync(sessionId, CancellationToken.None);
            return new UploadFinalizeResult(sessionId, session.Session.AssetId, write.ObjectKey, write.Length, write.Sha256, UploadSessionState.Completed);
        }
        catch
        {
            await _primary.DeleteAsync(objectKey, CancellationToken.None);
            throw;
        }
    }

    public async ValueTask<UploadHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var storage = await _primary.GetHealthAsync(cancellationToken);
        if (!storage.IsReady) return new UploadHealth(false, storage.Provider, storage.TargetId, storage.Detail);
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId = N'0003_p03_durable_upload';";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            var applied = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            return applied
                ? new UploadHealth(true, storage.Provider, storage.TargetId, "P03 upload schema and Primary Storage are ready.")
                : new UploadHealth(false, storage.Provider, storage.TargetId, "Primary Storage is ready but the P03 upload migration is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new UploadHealth(false, storage.Provider, storage.TargetId, $"Upload session database is unavailable: {ex.GetType().Name}.");
        }
    }

    private async ValueTask<UploadSessionSnapshot?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT SessionId, AssetId, Title, OriginalFileName, ExpectedLength, ExpectedSha256, ChunkSizeBytes,
                   ReceivedLength, State, IsQuarantined, PrimaryObjectKey, Error, ExpiresAtUtc
            FROM dbo.MamUploadSession WHERE SessionId = @SessionId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var contract = new UploadSessionContract(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(3), reader.GetInt64(4), reader.GetString(5).Trim(),
            reader.GetInt32(6), Utc(reader.GetDateTime(12)));
        return new UploadSessionSnapshot(
            contract,
            reader.GetString(2),
            reader.GetInt64(7),
            (UploadSessionState)reader.GetByte(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private async ValueTask<Guid?> FindOriginalByShaAsync(string sha, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT TOP (1) AssetId FROM dbo.MamMediaOriginal WHERE Sha256 = @Sha256 ORDER BY CreatedAtUtc, AssetId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Sha256", sha);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private async ValueTask SetStateAsync(Guid sessionId, UploadSessionState state, string? error, string? objectKey, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamUploadSession
            SET State = @State, Error = @Error, PrimaryObjectKey = COALESCE(@ObjectKey, PrimaryObjectKey), UpdatedAtUtc = @Now
            WHERE SessionId = @SessionId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@State", (byte)state);
        command.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("@ObjectKey", (object?)objectKey ?? DBNull.Value);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string BuildObjectKey(Guid assetId, string fileName, DateTimeOffset now)
    {
        var layout = _settings.Storage.Primary.PathLayout
            .Replace("yyyy", now.Year.ToString("0000"), StringComparison.Ordinal)
            .Replace("MM", now.Month.ToString("00"), StringComparison.Ordinal)
            .Replace("{AssetId}", assetId.ToString("D"), StringComparison.Ordinal);
        return string.Join('/', new[] { _settings.Storage.Primary.OriginalsPrefix.Trim('/'), layout.Trim('/'), fileName });
    }

    private static async Task<byte[]> ReadChunkAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream(Math.Min(maxBytes, 1024 * 1024));
        var buffer = new byte[128 * 1024];
        var total = 0;
        while (true)
        {
            var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maxBytes) throw Bad("chunk_too_large", "Chunk exceeds the configured chunk size.", 413);
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }

    private static string NormalizeTitle(string value)
    {
        var title = value?.Trim() ?? string.Empty;
        if (title.Length == 0) throw Bad("title_required", "Asset title is required.");
        if (title.Length > 300) throw Bad("title_too_long", "Asset title cannot exceed 300 characters.");
        return title;
    }

    private static string NormalizeFileName(string value)
    {
        var fileName = value?.Trim() ?? string.Empty;
        if (fileName.Length == 0 || fileName.Length > 240) throw Bad("invalid_file_name", "A safe original file name is required.");
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName.Contains('/') || fileName.Contains('\\') || fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw Bad("unsafe_file_name", "Paths and traversal segments are forbidden; supply only the original file name.");
        return fileName;
    }

    private static string NormalizeSha(string value)
    {
        var sha = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (sha.Length != 64 || sha.Any(character => !Uri.IsHexDigit(character)))
            throw Bad("invalid_sha256", "SHA-256 must contain exactly 64 hexadecimal characters.");
        return sha;
    }

    private static string NormalizeActor(string actorId) =>
        string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim()[..Math.Min(actorId.Trim().Length, 200)];

    private static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static UploadRequestException Bad(string code, string message, int statusCode = 400, Guid? existingAssetId = null) =>
        new(code, message, statusCode, existingAssetId);

    private static AuditEvent NewAudit(string actorId, string action, Guid entityId, string outcome, string? detail) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, NormalizeActor(actorId), action, "UploadSession", entityId.ToString("D"), outcome, detail);
}

internal sealed class ServerUploadStagingStore
{
    private readonly string _root;

    public ServerUploadStagingStore(string primaryRoot)
    {
        _root = Path.Combine(Path.GetFullPath(primaryRoot), ".upload-staging");
    }

    public async Task ReconcileLengthAsync(Guid sessionId, long committedLength, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(sessionId);
        if (!File.Exists(path))
        {
            if (committedLength != 0) throw new UploadRequestException("staging_inconsistent", "Server staging data is shorter than the committed upload offset.", 503);
            return;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        if (stream.Length < committedLength)
            throw new UploadRequestException("staging_inconsistent", "Server staging data is shorter than the committed upload offset.", 503);
        if (stream.Length > committedLength) stream.SetLength(committedLength);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task AppendAsync(Guid sessionId, long offset, byte[] payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        await using var stream = new FileStream(PathFor(sessionId), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != offset) throw new UploadRequestException("staging_offset_conflict", "Server staging offset does not match the committed session offset.", 409);
        stream.Position = offset;
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task TruncateAsync(Guid sessionId, long length, CancellationToken cancellationToken)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path)) return;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        if (stream.Length >= length) stream.SetLength(length);
        await stream.FlushAsync(cancellationToken);
    }

    public Task<Stream> OpenReadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(PathFor(sessionId), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public async Task<(long Length, string Sha256)> MeasureAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadAsync(sessionId, cancellationToken);
        using var sha = SHA256.Create();
        var digest = await sha.ComputeHashAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexString(digest).ToLowerInvariant());
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(sessionId);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        return Task.CompletedTask;
    }

    private string PathFor(Guid sessionId) => Path.Combine(_root, sessionId.ToString("N") + ".upload");
}
