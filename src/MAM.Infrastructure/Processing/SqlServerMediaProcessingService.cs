using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MAM.Application.Auditing;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Processing;

public sealed class SqlServerMediaProcessingService : IMediaProcessingService
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAuditSink _audit;
    private readonly MamSettings _settings;
    private readonly string _ffprobe;
    private readonly string _ffmpeg;

    public SqlServerMediaProcessingService(SqlServerConnectionFactory connections, IStorageObjectStore primary, IAuditSink audit, MamSettings settings)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _settings = settings;
        _ffprobe = ToolPath("MAM_FFPROBE_PATH", "ffprobe");
        _ffmpeg = ToolPath("MAM_FFMPEG_PATH", "ffmpeg");
    }

    public IReadOnlyList<ProcessingProfileDescriptor> Profiles => BuiltInProcessingProfiles.All;

    public async Task<ProcessingJobSnapshot> EnqueueAsync(Guid assetId, string profileId, string actorId, CancellationToken cancellationToken = default)
    {
        var profile = BuiltInProcessingProfiles.Find(profileId) ?? throw Error("unknown_processing_profile", "Unknown processing profile.");
        if (await ReadOriginalAsync(assetId, cancellationToken) is null)
            throw Error("primary_original_not_found", "A verified Primary original is required before processing.", 404);

        var existing = await FindJobAsync(assetId, profile.Id, profile.Version, cancellationToken);
        if (existing is not null) return existing;

        var id = Guid.NewGuid();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            INSERT dbo.MamProcessingJob(JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,CreatedAtUtc,UpdatedAtUtc)
            VALUES(@JobId,@AssetId,@ProfileId,@ProfileVersion,0,0,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@JobId", id);
        command.Parameters.AddWithValue("@AssetId", assetId);
        command.Parameters.AddWithValue("@ProfileId", profile.Id);
        command.Parameters.AddWithValue("@ProfileVersion", profile.Version);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            var raced = await FindJobAsync(assetId, profile.Id, profile.Version, cancellationToken);
            if (raced is not null) return raced;
            throw;
        }

        await AuditAsync(actorId, "processing.job.queued", id, "Success", $"asset={assetId:D};profile={profile.Id};v={profile.Version}", cancellationToken);
        return await GetJobAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessingJobSnapshot>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var sql = $"""
            SELECT TOP ({limit}) JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,LeaseOwner,LeaseExpiresAtUtc,
                LastHeartbeatAtUtc,LastError,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM dbo.MamProcessingJob ORDER BY CreatedAtUtc DESC,JobId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProcessingJobSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadJob(reader));
        return result;
    }

    public async Task<ProcessingJobSnapshot> RetryAsync(Guid jobId, string actorId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamProcessingJob SET State=0,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,
                LastError=NULL,CompletedAtUtc=NULL,UpdatedAtUtc=@Now
            WHERE JobId=@JobId AND State=3 AND AttemptCount < @MaxAttempts;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@JobId", jobId);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@MaxAttempts", _settings.Jobs.MaxAttempts);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw Error("processing_job_not_retryable", "Only failed jobs below the attempt limit can be retried.", 409);
        await AuditAsync(actorId, "processing.job.retry", jobId, "Success", null, cancellationToken);
        return await GetJobAsync(jobId, cancellationToken);
    }

    public async Task<ProcessingJobSnapshot?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default)
    {
        workerId = NormalizeWorker(workerId);
        var now = DateTime.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (_settings.Jobs.StaleJobRecoveryEnabled)
        {
            const string recoverSql = """
                UPDATE dbo.MamProcessingJob SET State=0,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,
                    LastError=N'Recovered stale processing lease.',UpdatedAtUtc=@Now
                WHERE State=1 AND LeaseExpiresAtUtc < @Now AND AttemptCount < @MaxAttempts;
                """;
            await using var recover = new SqlCommand(recoverSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            recover.Parameters.AddWithValue("@Now", now);
            recover.Parameters.AddWithValue("@MaxAttempts", _settings.Jobs.MaxAttempts);
            await recover.ExecuteNonQueryAsync(cancellationToken);
        }

        const string selectSql = """
            SELECT TOP (1) JobId FROM dbo.MamProcessingJob WITH (UPDLOCK,READPAST,ROWLOCK)
            WHERE State=0 AND AttemptCount < @MaxAttempts ORDER BY CreatedAtUtc,JobId;
            """;
        Guid? id;
        await using (var select = new SqlCommand(selectSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            select.Parameters.AddWithValue("@MaxAttempts", _settings.Jobs.MaxAttempts);
            id = await select.ExecuteScalarAsync(cancellationToken) as Guid?;
        }
        if (id is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        const string leaseSql = """
            UPDATE dbo.MamProcessingJob SET State=1,AttemptCount=AttemptCount+1,LeaseOwner=@WorkerId,
                LeaseExpiresAtUtc=@LeaseExpires,LastHeartbeatAtUtc=@Now,LastError=NULL,UpdatedAtUtc=@Now
            WHERE JobId=@JobId AND State=0;
            """;
        await using (var lease = new SqlCommand(leaseSql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            lease.Parameters.AddWithValue("@WorkerId", workerId);
            lease.Parameters.AddWithValue("@LeaseExpires", now.AddSeconds(Math.Max(1, _settings.Jobs.LeaseSeconds)));
            lease.Parameters.AddWithValue("@Now", now);
            lease.Parameters.AddWithValue("@JobId", id.Value);
            if (await lease.ExecuteNonQueryAsync(cancellationToken) != 1) throw Error("processing_lease_conflict", "Processing lease changed.", 409);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetJobAsync(id.Value, cancellationToken);
    }

    public async Task HeartbeatAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default)
    {
        workerId = NormalizeWorker(workerId);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamProcessingJob SET LeaseExpiresAtUtc=@LeaseExpires,LastHeartbeatAtUtc=@Now,UpdatedAtUtc=@Now
            WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("@LeaseExpires", now.AddSeconds(Math.Max(1, _settings.Jobs.LeaseSeconds)));
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@JobId", jobId);
        command.Parameters.AddWithValue("@WorkerId", workerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw Error("processing_lease_lost", "Processing lease was lost.", 409);
    }

    public async Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default)
    {
        workerId = NormalizeWorker(workerId);
        var current = await GetJobAsync(job.JobId, cancellationToken);
        if (current.State != ProcessingJobState.Leased || !string.Equals(current.LeaseOwner, workerId, StringComparison.Ordinal))
            throw Error("processing_lease_lost", "Job is not leased by this worker.", 409);
        var profile = BuiltInProcessingProfiles.Find(current.ProfileId) ?? throw Error("unknown_processing_profile", "Processing profile is unavailable.", 409);
        var original = await ReadOriginalAsync(current.AssetId, cancellationToken) ?? throw Error("primary_original_not_found", "Primary original is unavailable.", 404);
        var tempRoot = Path.Combine(Path.GetTempPath(), "mam-p04", current.JobId.ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, SafeLeaf(original.OriginalFileName));
        try
        {
            var before = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(before, original);
            await using (var input = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            await using (var output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);

            await HeartbeatAsync(current.JobId, workerId, cancellationToken);
            var technical = await InspectAsync(current.AssetId, sourcePath, original.OriginalFileName, cancellationToken);
            await SaveTechnicalAsync(technical, cancellationToken);

            DerivativeSnapshot? derivative = null;
            if (profile.GeneratesDerivative)
            {
                derivative = await GenerateAsync(current, profile, original, sourcePath, cancellationToken);
                await SaveDerivativeAsync(derivative, cancellationToken);
            }

            var after = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(after, original);
            if (!string.Equals(before.ActualSha256, after.ActualSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Primary original changed during processing.");

            await SetSucceededAsync(current.JobId, workerId, cancellationToken);
            await AuditAsync(workerId, "processing.job.completed", current.JobId, "Success",
                derivative is null ? $"asset={current.AssetId:D};technical-only" : $"asset={current.AssetId:D};derivative={derivative.DerivativeId:D};sha256={derivative.Sha256}", cancellationToken);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(current.JobId, workerId, ex.Message, CancellationToken.None);
            await AuditAsync(workerId, "processing.job.failed", current.JobId, "Failed", ex.GetType().Name, CancellationToken.None);
            throw;
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    public async Task<TechnicalMetadataSnapshot?> GetTechnicalMetadataAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT AssetId,MediaType,DurationSeconds,Width,Height,VideoCodec,AudioCodec,AudioChannels,AudioSampleRate,RawJson,InspectedAtUtc
            FROM dbo.MamTechnicalMetadata WHERE AssetId=@AssetId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTechnical(reader) : null;
    }

    public async Task<IReadOnlyList<DerivativeSnapshot>> ListDerivativesAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT DerivativeId,AssetId,ProfileId,ProfileVersion,ObjectKey,ContentType,Length,Sha256,CreatedAtUtc
            FROM dbo.MamMediaDerivative WHERE AssetId=@AssetId ORDER BY CreatedAtUtc,DerivativeId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DerivativeSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDerivative(reader));
        return result;
    }

    public async Task<ProcessingPreviewPayload?> OpenDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken = default)
    {
        var derivative = await ReadDerivativeAsync(assetId, derivativeId, cancellationToken);
        if (derivative is null) return null;
        var verified = await _primary.VerifyAsync(derivative.ObjectKey, derivative.Sha256, cancellationToken);
        if (!verified.Exists || !verified.ChecksumMatches || verified.Length != derivative.Length)
            throw Error("derivative_verification_failed", "Derivative verification failed before delivery.", 503);
        return new ProcessingPreviewPayload(await _primary.OpenReadAsync(derivative.ObjectKey, cancellationToken), derivative.ContentType,
            "preview" + Path.GetExtension(derivative.ObjectKey), derivative.Length, derivative.Sha256);
    }

    public async Task<ProcessingPreviewPayload?> OpenOriginalPreviewAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var original = await ReadOriginalAsync(assetId, cancellationToken);
        if (original is null) return null;
        if (!string.Equals(Path.GetExtension(original.OriginalFileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw Error("original_preview_not_allowed", "Only the approved PDF inline strategy may stream a Primary original directly.", 415);
        var verified = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
        RequireOriginal(verified, original);
        return new ProcessingPreviewPayload(await _primary.OpenReadAsync(original.ObjectKey, cancellationToken), "application/pdf",
            original.OriginalFileName, original.Length, original.Sha256);
    }

    public async Task<ProcessingHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0004_p04_processing';";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
                return new ProcessingHealth(false, "SqlServer+FFmpeg", "P04 processing migration is not applied.");
            if (!await ToolAvailableAsync(_ffprobe, cancellationToken)) return new ProcessingHealth(false, "SqlServer+FFmpeg", "FFprobe is unavailable.");
            if (!await ToolAvailableAsync(_ffmpeg, cancellationToken)) return new ProcessingHealth(false, "SqlServer+FFmpeg", "FFmpeg is unavailable.");
            return new ProcessingHealth(true, "SqlServer+FFmpeg", "P04 durable processing, FFprobe and FFmpeg are ready.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new ProcessingHealth(false, "SqlServer+FFmpeg", $"Processing dependency unavailable: {ex.GetType().Name}.");
        }
    }

    private async Task<TechnicalMetadataSnapshot> InspectAsync(Guid assetId, string sourcePath, string originalName, CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetExtension(originalName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return new TechnicalMetadataSnapshot(assetId, "Document", null, null, null, null, null, null, null,
                JsonSerializer.Serialize(new { format = new { format_name = "pdf" }, streams = Array.Empty<object>() }), DateTimeOffset.UtcNow);

        var run = await RunToolAsync(_ffprobe, new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", sourcePath }, cancellationToken);
        if (run.ExitCode != 0) throw new InvalidOperationException("FFprobe inspection failed: " + Short(run.StdErr));
        using var document = JsonDocument.Parse(run.StdOut);
        var root = document.RootElement;
        JsonElement? video = null;
        JsonElement? audio = null;
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var type)) continue;
                if (type.GetString() == "video" && video is null) video = stream.Clone();
                if (type.GetString() == "audio" && audio is null) audio = stream.Clone();
            }
        }
        double? duration = null;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var durationNode) &&
            double.TryParse(durationNode.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) duration = seconds;
        var kind = video is not null ? (IsImage(originalName) ? "Image" : "Video") : audio is not null ? "Audio" : "Unknown";
        return new TechnicalMetadataSnapshot(assetId, kind, duration,
            IntProperty(video, "width"), IntProperty(video, "height"), StringProperty(video, "codec_name"), StringProperty(audio, "codec_name"),
            IntProperty(audio, "channels"), IntStringProperty(audio, "sample_rate"), run.StdOut, DateTimeOffset.UtcNow);
    }

    private async Task<DerivativeSnapshot> GenerateAsync(ProcessingJobSnapshot job, ProcessingProfileDescriptor profile, OriginalRecord original, string sourcePath, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "derivative" + profile.OutputExtension);
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", sourcePath, "-map_metadata", "-1" };
        switch (profile.Id)
        {
            case BuiltInProcessingProfiles.VideoProxy:
                args.AddRange(new[] { "-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", outputPath }); break;
            case BuiltInProcessingProfiles.ImagePreview:
                args.AddRange(new[] { "-frames:v", "1", "-vf", "scale=1280:-2:force_original_aspect_ratio=decrease", "-q:v", "3", outputPath }); break;
            case BuiltInProcessingProfiles.AudioPreview:
                args.AddRange(new[] { "-vn", "-c:a", "aac", "-b:a", "128k", outputPath }); break;
            default: throw Error("profile_has_no_derivative", "Selected profile does not generate a derivative.", 409);
        }
        var run = await RunToolAsync(_ffmpeg, args, cancellationToken);
        if (run.ExitCode != 0 || !File.Exists(outputPath)) throw new InvalidOperationException("FFmpeg derivative generation failed: " + Short(run.StdErr));
        var length = new FileInfo(outputPath).Length;
        var sha = await FileShaAsync(outputPath, cancellationToken);
        var key = DerivativeKey(job.AssetId, profile, original.Sha256);
        var existing = await _primary.VerifyAsync(key, sha, cancellationToken);
        if (!existing.Exists)
        {
            await using var source = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await _primary.WriteAsync(key, source, sha, cancellationToken);
            existing = await _primary.VerifyAsync(key, sha, cancellationToken);
        }
        if (!existing.Exists || !existing.ChecksumMatches || existing.Length != length) throw new InvalidDataException("Generated derivative verification failed.");
        return new DerivativeSnapshot(DerivativeId(job.AssetId, profile, original.Sha256), job.AssetId, profile.Id, profile.Version, key, profile.ContentType!, length, sha, DateTimeOffset.UtcNow);
    }

    private async Task SaveTechnicalAsync(TechnicalMetadataSnapshot m, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            IF EXISTS(SELECT 1 FROM dbo.MamTechnicalMetadata WHERE AssetId=@AssetId)
                UPDATE dbo.MamTechnicalMetadata SET MediaType=@MediaType,DurationSeconds=@Duration,Width=@Width,Height=@Height,VideoCodec=@VideoCodec,
                    AudioCodec=@AudioCodec,AudioChannels=@Channels,AudioSampleRate=@Rate,RawJson=@RawJson,InspectedAtUtc=@Inspected WHERE AssetId=@AssetId;
            ELSE
                INSERT dbo.MamTechnicalMetadata(AssetId,MediaType,DurationSeconds,Width,Height,VideoCodec,AudioCodec,AudioChannels,AudioSampleRate,RawJson,InspectedAtUtc)
                VALUES(@AssetId,@MediaType,@Duration,@Width,@Height,@VideoCodec,@AudioCodec,@Channels,@Rate,@RawJson,@Inspected);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", m.AssetId); command.Parameters.AddWithValue("@MediaType", m.MediaType);
        command.Parameters.AddWithValue("@Duration", (object?)m.DurationSeconds ?? DBNull.Value); command.Parameters.AddWithValue("@Width", (object?)m.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("@Height", (object?)m.Height ?? DBNull.Value); command.Parameters.AddWithValue("@VideoCodec", (object?)m.VideoCodec ?? DBNull.Value);
        command.Parameters.AddWithValue("@AudioCodec", (object?)m.AudioCodec ?? DBNull.Value); command.Parameters.AddWithValue("@Channels", (object?)m.AudioChannels ?? DBNull.Value);
        command.Parameters.AddWithValue("@Rate", (object?)m.AudioSampleRate ?? DBNull.Value); command.Parameters.AddWithValue("@RawJson", m.RawJson);
        command.Parameters.AddWithValue("@Inspected", m.InspectedAtUtc.UtcDateTime); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SaveDerivativeAsync(DerivativeSnapshot d, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.MamMediaDerivative WHERE AssetId=@AssetId AND ProfileId=@ProfileId AND ProfileVersion=@Version)
                INSERT dbo.MamMediaDerivative(DerivativeId,AssetId,ProfileId,ProfileVersion,ObjectKey,ContentType,Length,Sha256,CreatedAtUtc)
                VALUES(@DerivativeId,@AssetId,@ProfileId,@Version,@ObjectKey,@ContentType,@Length,@Sha256,@CreatedAtUtc);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@DerivativeId", d.DerivativeId); command.Parameters.AddWithValue("@AssetId", d.AssetId);
        command.Parameters.AddWithValue("@ProfileId", d.ProfileId); command.Parameters.AddWithValue("@Version", d.ProfileVersion);
        command.Parameters.AddWithValue("@ObjectKey", d.ObjectKey); command.Parameters.AddWithValue("@ContentType", d.ContentType);
        command.Parameters.AddWithValue("@Length", d.Length); command.Parameters.AddWithValue("@Sha256", d.Sha256);
        command.Parameters.AddWithValue("@CreatedAtUtc", d.CreatedAtUtc.UtcDateTime); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SetSucceededAsync(Guid jobId, string workerId, CancellationToken cancellationToken) =>
        await UpdateLeaseStateAsync(jobId, workerId, 2, null, true, cancellationToken);

    private async Task SetFailedAsync(Guid jobId, string workerId, string error, CancellationToken cancellationToken)
    {
        try { await UpdateLeaseStateAsync(jobId, workerId, 3, Short(error, 1900), false, cancellationToken); } catch { }
    }

    private async Task UpdateLeaseStateAsync(Guid jobId, string workerId, byte state, string? error, bool completed, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamProcessingJob SET State=@State,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,
                LastError=@Error,CompletedAtUtc=CASE WHEN @Completed=1 THEN @Now ELSE CompletedAtUtc END,UpdatedAtUtc=@Now
            WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@State", state); command.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("@Completed", completed); command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@JobId", jobId); command.Parameters.AddWithValue("@WorkerId", workerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw Error("processing_lease_lost", "Processing lease was lost before state commit.", 409);
    }

    private async Task<OriginalRecord?> ReadOriginalAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT AssetId,ObjectKey,OriginalFileName,Length,Sha256 FROM dbo.MamMediaOriginal WHERE AssetId=@AssetId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new OriginalRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4).Trim()) : null;
    }

    private async Task<ProcessingJobSnapshot?> FindJobAsync(Guid assetId, string profileId, int version, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,LeaseOwner,LeaseExpiresAtUtc,LastHeartbeatAtUtc,LastError,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM dbo.MamProcessingJob WHERE AssetId=@AssetId AND ProfileId=@ProfileId AND ProfileVersion=@Version;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@ProfileId", profileId); command.Parameters.AddWithValue("@Version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    private async Task<ProcessingJobSnapshot> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT JobId,AssetId,ProfileId,ProfileVersion,State,AttemptCount,LeaseOwner,LeaseExpiresAtUtc,LastHeartbeatAtUtc,LastError,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM dbo.MamProcessingJob WHERE JobId=@JobId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@JobId", jobId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw Error("processing_job_not_found", "Processing job was not found.", 404); return ReadJob(reader);
    }

    private async Task<DerivativeSnapshot?> ReadDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT DerivativeId,AssetId,ProfileId,ProfileVersion,ObjectKey,ContentType,Length,Sha256,CreatedAtUtc FROM dbo.MamMediaDerivative WHERE AssetId=@AssetId AND DerivativeId=@DerivativeId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@DerivativeId", derivativeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadDerivative(reader) : null;
    }

    private static ProcessingJobSnapshot ReadJob(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetInt32(3), (ProcessingJobState)r.GetByte(4), r.GetInt32(5),
        S(r, 6), D(r, 7), D(r, 8), S(r, 9), Utc(r.GetDateTime(10)), Utc(r.GetDateTime(11)), D(r, 12));
    private static TechnicalMetadataSnapshot ReadTechnical(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2),
        I(r, 3), I(r, 4), S(r, 5), S(r, 6), I(r, 7), I(r, 8), r.GetString(9), Utc(r.GetDateTime(10)));
    private static DerivativeSnapshot ReadDerivative(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetString(7).Trim(), Utc(r.GetDateTime(8)));

    private static void RequireOriginal(StorageVerificationResult v, OriginalRecord o)
    {
        if (!v.Exists || !v.ChecksumMatches || v.Length != o.Length) throw new InvalidDataException("Primary original verification failed.");
    }

    private string DerivativeKey(Guid assetId, ProcessingProfileDescriptor p, string sourceSha) =>
        string.Join('/', _settings.Storage.Primary.DerivativesPrefix.Trim('/'), assetId.ToString("D"), $"{p.Id}-v{p.Version}", sourceSha[..16] + p.OutputExtension);
    private static Guid DerivativeId(Guid assetId, ProcessingProfileDescriptor p, string sourceSha) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{assetId:D}|{p.Id}|{p.Version}|{sourceSha}"))[..16]);
    private static bool IsImage(string name) => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    private static string ToolPath(string variable, string fallback) => Environment.GetEnvironmentVariable(variable)?.Trim() is { Length: > 0 } value ? value : fallback;
    private static string SafeLeaf(string name) => Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } leaf ? leaf : "source.bin";
    private static string NormalizeWorker(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Worker ID is required.") : value.Trim()[..Math.Min(value.Trim().Length, 200)];
    private static string Short(string value, int max = 800) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];
    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static DateTimeOffset? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Utc(r.GetDateTime(i));
    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));
    private static int? IntProperty(JsonElement? e, string n) => e is JsonElement x && x.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : null;
    private static int? IntStringProperty(JsonElement? e, string n) => e is JsonElement x && x.TryGetProperty(n, out var v) && int.TryParse(v.GetString(), out var i) ? i : null;
    private static string? StringProperty(JsonElement? e, string n) => e is JsonElement x && x.TryGetProperty(n, out var v) ? v.GetString() : null;
    private static ProcessingRequestException Error(string code, string message, int status = 400) => new(code, message, status);

    private static async Task<string> FileShaAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create(); return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task<ToolResult> RunToolAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo { FileName = file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new InvalidOperationException("Unable to start processing tool."); }
        catch (Win32Exception ex) { throw new InvalidOperationException($"Required processing tool '{file}' is unavailable.", ex); }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken); var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken); return new ToolResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task<bool> ToolAvailableAsync(string file, CancellationToken cancellationToken)
    {
        try { return (await RunToolAsync(file, new[] { "-version" }, cancellationToken)).ExitCode == 0; } catch { return false; }
    }

    private ValueTask AuditAsync(string actor, string action, Guid id, string outcome, string? detail, CancellationToken cancellationToken) =>
        _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), action, "ProcessingJob", id.ToString("D"), outcome, detail), cancellationToken);

    private sealed record OriginalRecord(Guid AssetId, string ObjectKey, string OriginalFileName, long Length, string Sha256);
    private sealed record ToolResult(int ExitCode, string StdOut, string StdErr);
}

public sealed class UnavailableMediaProcessingService : IMediaProcessingService
{
    private readonly string _detail;
    public UnavailableMediaProcessingService(string detail) => _detail = detail;
    public IReadOnlyList<ProcessingProfileDescriptor> Profiles => BuiltInProcessingProfiles.All;
    private ProcessingRequestException Failure() => new("processing_unavailable", _detail, 503);
    public Task<ProcessingJobSnapshot> EnqueueAsync(Guid assetId, string profileId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<ProcessingJobSnapshot>(Failure());
    public Task<IReadOnlyList<ProcessingJobSnapshot>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ProcessingJobSnapshot>>(Failure());
    public Task<ProcessingJobSnapshot> RetryAsync(Guid jobId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<ProcessingJobSnapshot>(Failure());
    public Task<ProcessingJobSnapshot?> LeaseNextAsync(string workerId, CancellationToken cancellationToken = default) => Task.FromException<ProcessingJobSnapshot?>(Failure());
    public Task HeartbeatAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default) => Task.FromException(Failure());
    public Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default) => Task.FromException(Failure());
    public Task<TechnicalMetadataSnapshot?> GetTechnicalMetadataAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<TechnicalMetadataSnapshot?>(Failure());
    public Task<IReadOnlyList<DerivativeSnapshot>> ListDerivativesAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<DerivativeSnapshot>>(Failure());
    public Task<ProcessingPreviewPayload?> OpenDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken = default) => Task.FromException<ProcessingPreviewPayload?>(Failure());
    public Task<ProcessingPreviewPayload?> OpenOriginalPreviewAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<ProcessingPreviewPayload?>(Failure());
    public Task<ProcessingHealth> GetHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProcessingHealth(false, "Unavailable", _detail));
}
