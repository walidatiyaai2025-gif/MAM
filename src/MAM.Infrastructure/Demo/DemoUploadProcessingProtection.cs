using System.Security.Cryptography;
using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Protection;
using MAM.Application.Storage;
using MAM.Application.Uploads;
using MAM.Domain.Assets;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Demo;

public sealed class DemoDurableUploadService(
    DemoSqliteDatabase database,
    IAssetCatalog catalog,
    IStorageObjectStore primary,
    IAuditSink audit,
    MamSettings settings) : IDurableUploadService
{
    private readonly string _tempRoot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(settings.Database.SqlitePath))!, "uploads");

    public async ValueTask<UploadSessionSnapshot> CreateSessionAsync(CreateUploadSessionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        Directory.CreateDirectory(_tempRoot);
        var sessionId = Guid.NewGuid();
        var assetId = AssetId.New();
        var created = await catalog.CreateWithIdAsync(assetId, request.Title, actorId, cancellationToken);
        if (created.Status != CatalogMutationStatus.Created)
            throw new UploadRequestException("asset_create_failed", created.Error ?? "The asset could not be created.", 409);

        var extension = System.IO.Path.GetExtension(request.OriginalFileName).ToLowerInvariant();
        var allowed = settings.Upload.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        var quarantined = !allowed && settings.Upload.QuarantineUnknownFiles;
        if (!allowed && !quarantined) throw new UploadRequestException("file_type_not_allowed", "The uploaded file type is not allowed.", 415);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(settings.Upload.SessionExpiryHours);
        var chunkSize = checked(settings.Upload.ChunkSizeMB * 1024 * 1024);
        var tempPath = System.IO.Path.Combine(_tempRoot, sessionId.ToString("N") + ".part");
        await using (File.Create(tempPath)) { }

        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DemoUploadSession(SessionId,AssetId,Title,OriginalFileName,ExpectedLength,ExpectedSha256,ChunkSizeBytes,ReceivedLength,State,IsQuarantined,TempPath,ExpiresAtUtc,CreatedAtUtc)
            VALUES($session,$asset,$title,$name,$length,$sha,$chunk,0,0,$quarantine,$temp,$expires,$created);
            """;
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$asset", assetId.Value.ToString("D"));
        command.Parameters.AddWithValue("$title", request.Title.Trim());
        command.Parameters.AddWithValue("$name", SafeFileName(request.OriginalFileName));
        command.Parameters.AddWithValue("$length", request.ExpectedLength);
        command.Parameters.AddWithValue("$sha", NormalizeSha(request.ExpectedSha256));
        command.Parameters.AddWithValue("$chunk", chunkSize);
        command.Parameters.AddWithValue("$quarantine", quarantined ? 1 : 0);
        command.Parameters.AddWithValue("$temp", tempPath);
        command.Parameters.AddWithValue("$expires", DemoSqliteDatabase.ToDb(expires));
        command.Parameters.AddWithValue("$created", DemoSqliteDatabase.ToDb(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), now, actorId, "upload.session.created", "UploadSession", sessionId.ToString("D"), "Success", $"asset={assetId};provider=SqliteDemo"), cancellationToken);
        return await GetSessionAsync(sessionId, cancellationToken);
    }

    public async ValueTask<UploadSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId,AssetId,OriginalFileName,ExpectedLength,ExpectedSha256,ChunkSizeBytes,ExpiresAtUtc,Title,ReceivedLength,State,IsQuarantined,PrimaryObjectKey,Error
            FROM DemoUploadSession WHERE SessionId=$id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new UploadRequestException("upload_session_not_found", "Upload session was not found.", 404);
        var expires = DemoSqliteDatabase.FromDb(reader.GetString(6));
        var state = (UploadSessionState)reader.GetInt32(9);
        if (state == UploadSessionState.Receiving && expires <= DateTimeOffset.UtcNow) state = UploadSessionState.Expired;
        var contract = new UploadSessionContract(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetInt32(5), expires);
        return new UploadSessionSnapshot(contract, reader.GetString(7), reader.GetInt64(8), state, reader.GetInt32(10) != 0, reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    public async ValueTask<UploadChunkResult> PutChunkAsync(Guid sessionId, long offset, string expectedChunkSha256, Stream body, string actorId, CancellationToken cancellationToken = default)
    {
        var state = await GetMutableSessionAsync(sessionId, cancellationToken);
        if (state.State != UploadSessionState.Receiving) throw new UploadRequestException("upload_session_not_receiving", "Upload session is not receiving data.", 409);
        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new UploadRequestException("upload_session_expired", "Upload session has expired.", 410);
        if (offset != state.ReceivedLength) throw new UploadRequestException("upload_offset_conflict", $"Expected offset {state.ReceivedLength}.", 409);

        await using var memory = new MemoryStream();
        await body.CopyToAsync(memory, cancellationToken);
        if (memory.Length <= 0 || memory.Length > state.ChunkSizeBytes) throw new UploadRequestException("invalid_chunk_length", "Chunk length is outside the allowed range.");
        if (state.ReceivedLength + memory.Length > state.ExpectedLength) throw new UploadRequestException("upload_too_large", "Chunk exceeds the declared upload length.");
        var bytes = memory.ToArray();
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!FixedSha(actual, expectedChunkSha256)) throw new UploadRequestException("chunk_checksum_mismatch", "Chunk SHA-256 did not match.", 422);
        await using (var file = new FileStream(state.TempPath, FileMode.Append, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            await file.WriteAsync(bytes, cancellationToken);

        var received = state.ReceivedLength + bytes.LongLength;
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DemoUploadSession SET ReceivedLength=$received WHERE SessionId=$id AND ReceivedLength=$expected;";
        command.Parameters.AddWithValue("$received", received);
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$expected", state.ReceivedLength);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new UploadRequestException("upload_concurrency_conflict", "Another request changed the upload session.", 409);
        return new UploadChunkResult(sessionId, received, state.ExpectedLength, received == state.ExpectedLength, new UploadChunkReceipt(offset, bytes.Length, actual));
    }

    public async ValueTask<UploadFinalizeResult> FinalizeAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default)
    {
        var state = await GetMutableSessionAsync(sessionId, cancellationToken);
        if (state.State != UploadSessionState.Receiving) throw new UploadRequestException("upload_session_not_receiving", "Upload session cannot be finalized from its current state.", 409);
        if (state.ReceivedLength != state.ExpectedLength) throw new UploadRequestException("upload_incomplete", "The complete declared file has not been received.", 409);
        string actual;
        await using (var source = File.OpenRead(state.TempPath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
        if (!FixedSha(actual, state.ExpectedSha256))
        {
            await SetFailureAsync(sessionId, "Full-file SHA-256 mismatch.", cancellationToken);
            throw new UploadRequestException("file_checksum_mismatch", "Uploaded file SHA-256 did not match.", 422);
        }

        await using (var connection = await database.OpenAsync(cancellationToken))
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.CommandText = "SELECT AssetId FROM DemoAsset WHERE OriginalSha256=$sha AND AssetId<>$asset LIMIT 1;";
            duplicate.Parameters.AddWithValue("$sha", actual);
            duplicate.Parameters.AddWithValue("$asset", state.AssetId.ToString("D"));
            var duplicateId = await duplicate.ExecuteScalarAsync(cancellationToken) as string;
            if (Guid.TryParse(duplicateId, out var existing)) throw new UploadRequestException("duplicate_original", "An asset with the same original content already exists.", 409, existing);
        }

        var now = DateTimeOffset.UtcNow;
        var objectKey = $"{settings.Storage.Primary.OriginalsPrefix}/{now:yyyy/MM}/{state.AssetId:D}/{SafeFileName(state.OriginalFileName)}";
        await using (var source = File.OpenRead(state.TempPath)) await primary.WriteAsync(objectKey, source, actual, cancellationToken);
        var backupKey = objectKey;
        await CopyToBackupAsync(objectKey, backupKey, actual, cancellationToken);

        await using (var connection = await database.OpenAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    UPDATE DemoAsset SET OriginalFileName=$name,OriginalObjectKey=$key,OriginalLength=$length,OriginalSha256=$sha,MediaKind=$kind,UpdatedAtUtc=$now
                    WHERE AssetId=$asset;
                    UPDATE DemoUploadSession SET State=$state,PrimaryObjectKey=$key WHERE SessionId=$session;
                    INSERT INTO DemoProtection(AssetId,PrimaryObjectKey,BackupObjectKey,ExpectedLength,ExpectedSha256,State,AttemptCount,VerifiedAtUtc,LastIntegrityCheckAtUtc,LastError)
                    VALUES($asset,$key,$backup,$length,$sha,1,1,$now,$now,NULL)
                    ON CONFLICT(AssetId) DO UPDATE SET PrimaryObjectKey=excluded.PrimaryObjectKey,BackupObjectKey=excluded.BackupObjectKey,ExpectedLength=excluded.ExpectedLength,ExpectedSha256=excluded.ExpectedSha256,State=1,AttemptCount=DemoProtection.AttemptCount+1,VerifiedAtUtc=excluded.VerifiedAtUtc,LastIntegrityCheckAtUtc=excluded.LastIntegrityCheckAtUtc,LastError=NULL;
                    """;
                command.Parameters.AddWithValue("$name", state.OriginalFileName);
                command.Parameters.AddWithValue("$key", objectKey);
                command.Parameters.AddWithValue("$backup", backupKey);
                command.Parameters.AddWithValue("$length", state.ExpectedLength);
                command.Parameters.AddWithValue("$sha", actual);
                command.Parameters.AddWithValue("$kind", MediaKinds.FromFileName(state.OriginalFileName));
                command.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(now));
                command.Parameters.AddWithValue("$asset", state.AssetId.ToString("D"));
                command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
                command.Parameters.AddWithValue("$state", (int)(state.IsQuarantined ? UploadSessionState.Quarantined : UploadSessionState.Completed));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        TryDelete(state.TempPath);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), now, actorId, "upload.session.finalized", "MediaAsset", state.AssetId.ToString("D"), "Success", $"provider=SqliteDemo;sha256={actual};protected=true"), cancellationToken);
        return new UploadFinalizeResult(sessionId, state.AssetId, objectKey, state.ExpectedLength, actual, state.IsQuarantined ? UploadSessionState.Quarantined : UploadSessionState.Completed);
    }

    public async ValueTask<UploadHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var storage = await primary.GetHealthAsync(cancellationToken);
        await database.EnsureInitializedAsync(cancellationToken);
        return new UploadHealth(storage.IsReady, "SqliteDemo+FileSystem", primary.TargetId, storage.IsReady ? "Offline demo upload store is ready." : storage.Detail);
    }

    private async Task CopyToBackupAsync(string primaryKey, string backupKey, string expectedSha, CancellationToken cancellationToken)
    {
        var backupRoot = System.IO.Path.GetFullPath(settings.Storage.Backup.Root);
        var target = ResolveSafe(backupRoot, backupKey);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        await using var source = await primary.OpenReadAsync(primaryKey, cancellationToken);
        await using var destination = new FileStream(target + ".writing", FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!FixedSha(actual, expectedSha)) throw new InvalidDataException("Demo Backup checksum mismatch.");
        destination.Close();
        File.Move(target + ".writing", target, true);
    }

    private async Task<MutableSession> GetMutableSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AssetId,OriginalFileName,ExpectedLength,ExpectedSha256,ChunkSizeBytes,ReceivedLength,State,IsQuarantined,TempPath,ExpiresAtUtc FROM DemoUploadSession WHERE SessionId=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new UploadRequestException("upload_session_not_found", "Upload session was not found.", 404);
        return new MutableSession(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt64(5), (UploadSessionState)reader.GetInt32(6), reader.GetInt32(7) != 0, reader.GetString(8), DemoSqliteDatabase.FromDb(reader.GetString(9)));
    }

    private async Task SetFailureAsync(Guid sessionId, string error, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DemoUploadSession SET State=$state,Error=$error WHERE SessionId=$id;";
        command.Parameters.AddWithValue("$state", (int)UploadSessionState.Failed);
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void ValidateRequest(CreateUploadSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new UploadRequestException("title_required", "Asset title is required.");
        if (request.ExpectedLength <= 0 || request.ExpectedLength > settings.Upload.MaxFileSizeGB * 1024L * 1024L * 1024L) throw new UploadRequestException("invalid_length", "Upload size is outside the configured limit.");
        _ = NormalizeSha(request.ExpectedSha256);
        _ = SafeFileName(request.OriginalFileName);
    }

    private static string SafeFileName(string name)
    {
        var value = System.IO.Path.GetFileName(name?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(value) || value != name?.Trim() || value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) throw new UploadRequestException("invalid_file_name", "Original file name is invalid.");
        return value;
    }
    private static string NormalizeSha(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c))) throw new UploadRequestException("invalid_sha256", "SHA-256 must contain exactly 64 hexadecimal characters.");
        return normalized;
    }
    private static bool FixedSha(string a, string b)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(NormalizeSha(a)), Convert.FromHexString(NormalizeSha(b))); }
        catch { return false; }
    }
    private static string ResolveSafe(string root, string key)
    {
        var fullRoot = root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, key.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup path escaped its configured root.");
        return path;
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed record MutableSession(Guid AssetId,string OriginalFileName,long ExpectedLength,string ExpectedSha256,int ChunkSizeBytes,long ReceivedLength,UploadSessionState State,bool IsQuarantined,string TempPath,DateTimeOffset ExpiresAtUtc);
}

public sealed class DemoMediaProcessingService(DemoSqliteDatabase database, IStorageObjectStore primary) : IMediaProcessingService
{
    public IReadOnlyList<ProcessingProfileDescriptor> Profiles => BuiltInProcessingProfiles.All;

    public async Task<ProcessingJobSnapshot> EnqueueAsync(Guid assetId, string profileId, string actorId, CancellationToken cancellationToken = default)
    {
        var profile = BuiltInProcessingProfiles.Find(profileId) ?? throw new ProcessingRequestException("unknown_profile", "Processing profile is not available.", 404);
        await EnsureAssetAsync(assetId, cancellationToken);
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO DemoProcessingJob(JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc) VALUES($id,$asset,$profile,$version,2,1,$now,$now,$now);";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$asset", assetId.ToString("D")); command.Parameters.AddWithValue("$profile", profile.Id); command.Parameters.AddWithValue("$version", profile.Version); command.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new ProcessingJobSnapshot(id, assetId, profile.Id, profile.Version, ProcessingJobState.Succeeded, 1, null, null, null, null, now, now, now);
    }

    public async Task<IReadOnlyList<ProcessingJobSnapshot>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,LastError,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc FROM DemoProcessingJob ORDER BY CreatedAtUtc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var rows = new List<ProcessingJobSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadJob(reader));
        return rows;
    }

    public async Task<ProcessingJobSnapshot> RetryAsync(Guid jobId, string actorId, CancellationToken cancellationToken = default)
    {
        var existing = (await ListJobsAsync(500, cancellationToken)).FirstOrDefault(x => x.JobId == jobId) ?? throw new ProcessingRequestException("job_not_found", "Processing job was not found.", 404);
        return await EnqueueAsync(existing.AssetId, existing.ProfileId, actorId, cancellationToken);
    }
    public Task<ProcessingJobSnapshot?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default) => Task.FromResult<ProcessingJobSnapshot?>(null);
    public Task HeartbeatAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<TechnicalMetadataSnapshot?> GetTechnicalMetadataAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MediaKind,OriginalFileName,UpdatedAtUtc FROM DemoAsset WHERE AssetId=$id AND OriginalObjectKey IS NOT NULL;"; command.Parameters.AddWithValue("$id", assetId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return null;
        var kind = reader.IsDBNull(0) ? MediaKinds.FromFileName(reader.IsDBNull(1) ? null : reader.GetString(1)) : reader.GetString(0);
        return new TechnicalMetadataSnapshot(assetId, kind, null, null, null, null, null, null, null, "{\"provider\":\"SqliteDemo\"}", DemoSqliteDatabase.FromDb(reader.GetString(2)));
    }
    public Task<IReadOnlyList<DerivativeSnapshot>> ListDerivativesAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DerivativeSnapshot>>(Array.Empty<DerivativeSnapshot>());
    public Task<ProcessingPreviewPayload?> OpenDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken = default) => Task.FromResult<ProcessingPreviewPayload?>(null);

    public async Task<ProcessingPreviewPayload?> OpenOriginalPreviewAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OriginalObjectKey,OriginalFileName,OriginalLength,OriginalSha256 FROM DemoAsset WHERE AssetId=$id;"; command.Parameters.AddWithValue("$id", assetId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        var key=reader.GetString(0); var name=reader.GetString(1); var length=reader.GetInt64(2); var sha=reader.GetString(3); var stream=await primary.OpenReadAsync(key,cancellationToken);
        return new ProcessingPreviewPayload(stream, ContentType(name), name, length, sha);
    }
    public Task<ProcessingHealth> GetHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProcessingHealth(true, "SqliteDemo", "Offline demo processing queue is available; heavyweight OCR/transcription executors are not started automatically."));

    private async Task EnsureAssetAsync(Guid id, CancellationToken ct) { await using var c=await database.OpenAsync(ct); await using var q=c.CreateCommand(); q.CommandText="SELECT COUNT(*) FROM DemoAsset WHERE AssetId=$id;"; q.Parameters.AddWithValue("$id",id.ToString("D")); if(Convert.ToInt64(await q.ExecuteScalarAsync(ct))==0) throw new ProcessingRequestException("asset_not_found","Asset was not found.",404); }
    private static ProcessingJobSnapshot ReadJob(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetInt32(3),(ProcessingJobState)r.GetInt32(4),r.GetInt32(5),null,null,null,r.IsDBNull(6)?null:r.GetString(6),DemoSqliteDatabase.FromDb(r.GetString(7)),DemoSqliteDatabase.FromDb(r.GetString(8)),r.IsDBNull(9)?null:DemoSqliteDatabase.FromDb(r.GetString(9)));
    private static string ContentType(string n)=>System.IO.Path.GetExtension(n).ToLowerInvariant() switch { ".mp4"=>"video/mp4", ".mp3"=>"audio/mpeg", ".wav"=>"audio/wav", ".jpg" or ".jpeg"=>"image/jpeg", ".png"=>"image/png", ".pdf"=>"application/pdf", ".txt"=>"text/plain; charset=utf-8", _=>"application/octet-stream" };
}

public sealed class DemoBackupProtectionService(DemoSqliteDatabase database, MamSettings settings) : IBackupProtectionService
{
    public Task<BackupProtectionHealth> GetHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BackupProtectionHealth(true, settings.Storage.Primary.Id, settings.Storage.Backup.Id, "Offline demo Primary and Backup folders are configured locally."));
    public Task<int> QueueEligibleOriginalsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<BackupJobLease?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default) => Task.FromResult<BackupJobLease?>(null);
    public async Task<BackupProtectionRecord> ExecuteAsync(BackupJobLease lease, string workerId, CancellationToken cancellationToken = default) => await GetAsync(lease.AssetId,cancellationToken) ?? throw new InvalidOperationException("Demo protection record not found.");
    public async Task<BackupProtectionRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand(); q.CommandText="SELECT PrimaryObjectKey,BackupObjectKey,ExpectedLength,ExpectedSha256,State,AttemptCount,VerifiedAtUtc,LastIntegrityCheckAtUtc,LastError FROM DemoProtection WHERE AssetId=$id;"; q.Parameters.AddWithValue("$id",assetId.ToString("D")); await using var r=await q.ExecuteReaderAsync(cancellationToken); if(!await r.ReadAsync(cancellationToken)) return null;
        return new BackupProtectionRecord(assetId,settings.Storage.Primary.Id,r.GetString(0),settings.Storage.Backup.Id,r.GetString(1),r.GetInt64(2),r.GetString(3),(BackupProtectionState)r.GetInt32(4),r.GetInt32(5),r.IsDBNull(6)?null:DemoSqliteDatabase.FromDb(r.GetString(6)),r.IsDBNull(7)?null:DemoSqliteDatabase.FromDb(r.GetString(7)),r.IsDBNull(8)?null:r.GetString(8));
    }
    public async Task<BackupProtectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand(); q.CommandText="SELECT SUM(CASE WHEN State=0 THEN 1 ELSE 0 END),SUM(CASE WHEN State=1 THEN 1 ELSE 0 END),SUM(CASE WHEN State=2 THEN 1 ELSE 0 END),SUM(CASE WHEN State=3 THEN 1 ELSE 0 END) FROM DemoProtection;"; await using var r=await q.ExecuteReaderAsync(cancellationToken); await r.ReadAsync(cancellationToken); long V(int i)=>r.IsDBNull(i)?0:r.GetInt64(i); return new BackupProtectionSummary(V(0),V(1),V(2),V(3),DateTimeOffset.UtcNow);
    }
    public Task<int> QueueIntegrityRechecksAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default)=>Task.FromResult(0);
}
