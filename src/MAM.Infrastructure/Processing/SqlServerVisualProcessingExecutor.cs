using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Processing;

public sealed class SqlServerVisualProcessingExecutor
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAuditSink _audit;
    private readonly IMediaProcessingService _processing;
    private readonly IDiscoveryService _discovery;
    private readonly IVisualSearchService _visual;
    private readonly string _ffmpeg;

    public SqlServerVisualProcessingExecutor(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IAuditSink audit,
        IMediaProcessingService processing,
        IDiscoveryService discovery,
        IVisualSearchService visual)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _processing = processing;
        _discovery = discovery;
        _visual = visual;
        _ffmpeg = Environment.GetEnvironmentVariable("MAM_FFMPEG_PATH")?.Trim() is { Length: > 0 } configured ? configured : "ffmpeg";
    }

    public async Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default)
    {
        if (job.State != ProcessingJobState.Leased || !string.Equals(job.LeaseOwner, workerId, StringComparison.Ordinal))
            throw new ProcessingRequestException("processing_lease_lost", "Visual processing job is not leased by this worker.", 409);
        try
        {
            if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.VisualIndex, StringComparison.OrdinalIgnoreCase))
                await ProcessAssetIndexAsync(job, workerId, cancellationToken);
            else if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.VisualSegments, StringComparison.OrdinalIgnoreCase))
                await ProcessSegmentsAsync(job, workerId, cancellationToken);
            else
                throw new ProcessingRequestException("visual_profile_invalid", "Visual executor received an unsupported processing profile.", 409);

            await SetSucceededAsync(job.JobId, workerId, cancellationToken);
            await AuditAsync(workerId, "processing.visual.completed", job.JobId, "Success", $"asset={job.AssetId:D};profile={job.ProfileId}", cancellationToken);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(job.JobId, workerId, ex.Message, CancellationToken.None);
            try { await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Failed", 0, Short(ex.Message, 280), false, CancellationToken.None); } catch { }
            await AuditAsync(workerId, "processing.visual.failed", job.JobId, "Failed", ex.GetType().Name, CancellationToken.None);
            throw;
        }
    }

    private async Task ProcessAssetIndexAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken)
    {
        var mediaKind = await _discovery.GetAssetMediaKindAsync(job.AssetId, cancellationToken);
        if (!string.Equals(mediaKind, MediaKinds.Image, StringComparison.OrdinalIgnoreCase))
            throw new ProcessingRequestException("visual_index_media_type_not_supported", "Asset-level visual indexing supports image media.", 415);
        await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Running", 35, "Indexing image for visual similarity search.", false, cancellationToken);
        await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
        await _visual.IndexAssetAsync(job.AssetId, cancellationToken);
        await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Succeeded", 100, "Image visual index is searchable.", true, cancellationToken);
    }

    private async Task ProcessSegmentsAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken)
    {
        var transcript = await _discovery.GetTextAsync(job.AssetId, DiscoverySources.Transcript, cancellationToken)
            ?? throw new ProcessingRequestException("transcript_required", "Timestamped transcript is required before visual segment processing.", 409);
        if (transcript.Segments.Count == 0)
            throw new ProcessingRequestException("transcript_segments_required", "Transcript contains no timestamped segments.", 409);
        var mediaKind = await _discovery.GetAssetMediaKindAsync(job.AssetId, cancellationToken);
        if (string.Equals(mediaKind, MediaKinds.Audio, StringComparison.OrdinalIgnoreCase))
        {
            await _visual.RegisterSegmentsAsync(job.AssetId, DiscoverySources.Transcript, transcript.Segments, false, "Audio-only media has no visual frame.", cancellationToken);
            await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Succeeded", 100, "Audio transcript segments are registered without visual thumbnails.", true, cancellationToken);
            return;
        }
        if (!string.Equals(mediaKind, MediaKinds.Video, StringComparison.OrdinalIgnoreCase))
            throw new ProcessingRequestException("visual_segments_media_type_not_supported", "Transcript visual segments require video or audio media.", 415);

        var original = await ReadOriginalAsync(job.AssetId, cancellationToken)
            ?? throw new ProcessingRequestException("primary_original_not_found", "Primary original is unavailable.", 404);
        var verify = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
        if (!verify.Exists || !verify.ChecksumMatches || verify.Length != original.Length)
            throw new ProcessingRequestException("primary_original_verification_failed", "Primary original verification failed before visual segment processing.", 503);

        await _visual.RegisterSegmentsAsync(job.AssetId, DiscoverySources.Transcript, transcript.Segments, true, null, cancellationToken);
        await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Running", 5, "Preparing transcript segment thumbnails.", false, cancellationToken);
        var tempRoot = Path.Combine(Path.GetTempPath(), "mam-p12-visual", job.JobId.ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, SafeLeaf(original.FileName));
        try
        {
            await using (var source = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            await using (var target = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(target, cancellationToken);

            for (var i = 0; i < transcript.Segments.Count; i++)
            {
                var segment = transcript.Segments[i];
                if (segment.StartMs is null || segment.EndMs is null || segment.EndMs < segment.StartMs)
                    throw new InvalidDataException($"Transcript segment {segment.SegmentIndex} does not contain a valid time range.");
                var captureMs = segment.StartMs.Value + Math.Max(0, segment.EndMs.Value - segment.StartMs.Value) / 2;
                var framePath = Path.Combine(tempRoot, $"segment-{segment.SegmentIndex:D6}.jpg");
                await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
                var result = await RunFfmpegAsync(sourcePath, framePath, captureMs, cancellationToken);
                if (result.ExitCode != 0 || !File.Exists(framePath) || new FileInfo(framePath).Length == 0)
                    throw new InvalidDataException($"Unable to extract representative frame for transcript segment {segment.SegmentIndex}: {Short(result.StdErr, 500)}");
                await using var frame = File.OpenRead(framePath);
                await _visual.UpsertSegmentThumbnailAsync(job.AssetId, DiscoverySources.Transcript, segment.SegmentIndex, captureMs, frame, "image/jpeg", cancellationToken);
                var progress = Math.Clamp(10 + (int)Math.Round(((i + 1d) / transcript.Segments.Count) * 85d), 10, 95);
                await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Running", progress, $"Indexed visual segment {i + 1} of {transcript.Segments.Count}.", false, cancellationToken);
            }

            var after = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            if (!after.Exists || !after.ChecksumMatches || after.Length != original.Length)
                throw new InvalidDataException("Primary original changed or became unreadable during visual segment processing.");
            await _discovery.SetExtractionStatusAsync(job.AssetId, "visual", "Succeeded", 100, $"Indexed {transcript.Segments.Count} visual transcript segment(s).", true, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private async Task<ToolResult> RunFfmpegAsync(string sourcePath, string framePath, long captureMs, CancellationToken cancellationToken)
    {
        var seconds = (captureMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
        var info = new ProcessStartInfo
        {
            FileName = _ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-y", "-hide_banner", "-loglevel", "error", "-ss", seconds, "-i", sourcePath, "-frames:v", "1", "-vf", "scale='min(640,iw)':-2", "-q:v", "3", framePath }) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new InvalidOperationException("Unable to start FFmpeg for visual segment extraction."); }
        catch (Win32Exception ex) { throw new ProcessingRequestException("ffmpeg_unavailable", $"Required FFmpeg executable '{_ffmpeg}' is unavailable: {ex.Message}", 503); }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken); var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken); return new ToolResult(process.ExitCode, await stdout, await stderr);
    }

    private async Task<OriginalRecord?> ReadOriginalAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT ObjectKey,OriginalFileName,Length,Sha256 FROM dbo.MamMediaOriginal WHERE AssetId=@AssetId;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new OriginalRecord(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3).Trim()) : null;
    }

    private async Task SetSucceededAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "UPDATE dbo.MamProcessingJob SET State=2,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=NULL,CompletedAtUtc=@Now,UpdatedAtUtc=@Now WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds }; command.Parameters.AddWithValue("@Now", DateTime.UtcNow); command.Parameters.AddWithValue("@JobId", jobId); command.Parameters.AddWithValue("@WorkerId", workerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new ProcessingRequestException("processing_lease_lost", "Visual processing lease was lost before success commit.", 409);
    }
    private async Task SetFailedAsync(Guid jobId, string workerId, string error, CancellationToken cancellationToken)
    {
        try { await using var connection=await _connections.OpenAsync(cancellationToken);await using var command=new SqlCommand("UPDATE dbo.MamProcessingJob SET State=3,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=@Error,UpdatedAtUtc=@Now WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;",connection){CommandTimeout=_connections.CommandTimeoutSeconds};command.Parameters.AddWithValue("@Error",Short(error,1900));command.Parameters.AddWithValue("@Now",DateTime.UtcNow);command.Parameters.AddWithValue("@JobId",jobId);command.Parameters.AddWithValue("@WorkerId",workerId);await command.ExecuteNonQueryAsync(cancellationToken);} catch { }
    }
    private ValueTask AuditAsync(string actor,string action,Guid id,string outcome,string? detail,CancellationToken ct)=>_audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,string.IsNullOrWhiteSpace(actor)?"unknown":actor.Trim(),action,"ProcessingJob",id.ToString("D"),outcome,detail),ct);
    private static string SafeLeaf(string name)=>Path.GetFileName(name.Replace('\\','/')) is {Length:>0} leaf?leaf:"source.bin";
    private static string Short(string value,int max)=>string.IsNullOrEmpty(value)?string.Empty:value[..Math.Min(value.Length,max)];
    private sealed record OriginalRecord(string ObjectKey,string FileName,long Length,string Sha256);
    private sealed record ToolResult(int ExitCode,string StdOut,string StdErr);
}