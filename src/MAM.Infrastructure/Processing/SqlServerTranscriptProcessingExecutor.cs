using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Processing;

/// <summary>
/// Server-side timestamped speech transcription. The executor deliberately keeps the
/// Whisper implementation behind a CLI boundary so deployments can pin an approved
/// whisper.cpp build/model without exposing it to Desktop/Web clients.
/// </summary>
public sealed class SqlServerTranscriptProcessingExecutor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mxf", ".mkv", ".avi", ".webm", ".m4v",
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".wma"
    };

    private static readonly Regex TimestampLine = new(
        @"^(?<from>\d{2}:\d{2}(?::\d{2})?[\.,]\d{3})\s+-->\s+(?<to>\d{2}:\d{2}(?::\d{2})?[\.,]\d{3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAuditSink _audit;
    private readonly SqlServerMediaProcessingService _processing;
    private readonly IDiscoveryService _discovery;
    private readonly string _ffmpeg;
    private readonly string _whisper;
    private readonly string _model;
    private readonly string _language;

    public SqlServerTranscriptProcessingExecutor(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IAuditSink audit,
        SqlServerMediaProcessingService processing,
        IDiscoveryService discovery)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _processing = processing;
        _discovery = discovery;
        _ffmpeg = ToolPath("MAM_FFMPEG_PATH", "ffmpeg");
        _whisper = ToolPath("MAM_WHISPER_PATH", "whisper-cli");
        _model = Environment.GetEnvironmentVariable("MAM_WHISPER_MODEL_PATH")?.Trim() ?? string.Empty;
        _language = Environment.GetEnvironmentVariable("MAM_WHISPER_LANGUAGE")?.Trim() is { Length: > 0 } language ? language : "auto";
    }

    public async Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.ProfileId, BuiltInProcessingProfiles.TranscriptText, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Transcript executor received a non-transcript processing profile.", nameof(job));
        if (job.State != ProcessingJobState.Leased || !string.Equals(job.LeaseOwner, workerId, StringComparison.Ordinal))
            throw new ProcessingRequestException("processing_lease_lost", "Transcript job is not leased by this worker.", 409);
        if (string.IsNullOrWhiteSpace(_model) || !File.Exists(_model))
            throw new ProcessingRequestException("transcription_model_unavailable", "Set MAM_WHISPER_MODEL_PATH to an approved whisper.cpp model file on the Worker host.", 503);

        var original = await ReadOriginalAsync(job.AssetId, cancellationToken)
            ?? throw new ProcessingRequestException("primary_original_not_found", "Primary original is unavailable.", 404);
        if (!SupportedExtensions.Contains(Path.GetExtension(original.OriginalFileName)))
            throw new ProcessingRequestException("transcription_media_type_not_supported", "Timestamped transcription supports approved audio and video originals.", 415);

        var tempRoot = Path.Combine(Path.GetTempPath(), "mam-p12-transcript", job.JobId.ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, SafeLeaf(original.OriginalFileName));
        var wavPath = Path.Combine(tempRoot, "speech.wav");
        var outputPrefix = Path.Combine(tempRoot, "transcript");

        try
        {
            await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Running", 5,
                "Preparing verified Primary original.", false, cancellationToken);

            var before = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(before, original);
            await using (var input = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            await using (var output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);

            await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
            await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Running", 15,
                "Extracting speech audio.", false, cancellationToken);

            var extract = await RunToolAsync(_ffmpeg,
                new[] { "-y", "-hide_banner", "-loglevel", "error", "-i", sourcePath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath },
                cancellationToken);
            if (extract.ExitCode != 0 || !File.Exists(wavPath))
                throw new InvalidOperationException("Speech audio extraction failed: " + Short(extract.StdErr));

            await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
            await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Running", 30,
                "Running timestamped speech transcription.", false, cancellationToken);

            var transcribe = await RunToolAsync(_whisper,
                new[] { "-m", _model, "-f", wavPath, "-l", _language, "-ovtt", "-of", outputPrefix },
                cancellationToken);
            if (transcribe.ExitCode != 0)
                throw new InvalidOperationException("Speech transcription failed: " + Short(transcribe.StdErr));

            var vttPath = outputPrefix + ".vtt";
            if (!File.Exists(vttPath))
                throw new InvalidDataException("Transcription engine did not produce the expected WebVTT timeline.");

            await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
            await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Running", 85,
                "Indexing timestamped transcript.", false, cancellationToken);

            var vtt = await File.ReadAllTextAsync(vttPath, cancellationToken);
            var segments = ParseWebVtt(vtt);
            if (segments.Count == 0)
                throw new InvalidDataException("Transcription produced no timestamped speech segments.");
            var plainText = string.Join(Environment.NewLine, segments.Select(segment => segment.Text)).Trim();
            var transcriptSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText))).ToLowerInvariant();
            await _discovery.UpsertTextAsync(job.AssetId, DiscoverySources.Transcript, _language, plainText, transcriptSha, segments, cancellationToken);

            var after = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(after, original);
            if (!string.Equals(before.ActualSha256, after.ActualSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Primary original changed during transcription.");

            await SetSucceededAsync(job.JobId, workerId, cancellationToken);
            await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Succeeded", 100,
                $"Indexed {segments.Count} timestamped segment(s).", true, cancellationToken);
            await AuditAsync(workerId, "processing.transcript.completed", job.JobId, "Success",
                $"asset={job.AssetId:D};segments={segments.Count};language={_language};sha256={transcriptSha}", cancellationToken);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(job.JobId, workerId, ex.Message, CancellationToken.None);
            try
            {
                await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Transcript, "Failed", 0,
                    Short(ex.Message, 280), false, CancellationToken.None);
            }
            catch { }
            await AuditAsync(workerId, "processing.transcript.failed", job.JobId, "Failed", ex.GetType().Name, CancellationToken.None);
            throw;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static IReadOnlyList<TextSegmentSnapshot> ParseWebVtt(string value)
    {
        var lines = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new List<TextSegmentSnapshot>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = TimestampLine.Match(lines[i].Trim());
            if (!match.Success) continue;
            var start = ParseTimestamp(match.Groups["from"].Value);
            var end = ParseTimestamp(match.Groups["to"].Value);
            var text = new StringBuilder();
            for (i++; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]); i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Length > 0) text.Append(' ');
                text.Append(line);
            }
            var cleaned = text.ToString().Trim();
            if (cleaned.Length == 0) continue;
            result.Add(new TextSegmentSnapshot(result.Count, start, end, null, cleaned));
        }
        return result;
    }

    private static long ParseTimestamp(string value)
    {
        var normalized = value.Replace(',', '.');
        var parts = normalized.Split(':');
        if (parts.Length == 2)
        {
            var minutes = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            var seconds = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            return checked((long)Math.Round((minutes * 60d + seconds) * 1000d));
        }
        if (parts.Length == 3)
        {
            var hours = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            var minutes = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var seconds = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            return checked((long)Math.Round((hours * 3600d + minutes * 60d + seconds) * 1000d));
        }
        throw new FormatException("Invalid WebVTT timestamp.");
    }

    private async Task<OriginalRecord?> ReadOriginalAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "SELECT AssetId,ObjectKey,OriginalFileName,Length,Sha256 FROM dbo.MamMediaOriginal WHERE AssetId=@AssetId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new OriginalRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4).Trim())
            : null;
    }

    private async Task SetSucceededAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamProcessingJob
            SET State=2,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=NULL,CompletedAtUtc=@Now,UpdatedAtUtc=@Now
            WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@JobId", jobId);
        command.Parameters.AddWithValue("@WorkerId", workerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new ProcessingRequestException("processing_lease_lost", "Transcript processing lease was lost before success commit.", 409);
    }

    private async Task SetFailedAsync(Guid jobId, string workerId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE dbo.MamProcessingJob
                SET State=3,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=@Error,UpdatedAtUtc=@Now
                WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            command.Parameters.AddWithValue("@Error", Short(error, 1900));
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
            command.Parameters.AddWithValue("@JobId", jobId);
            command.Parameters.AddWithValue("@WorkerId", workerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { }
    }

    private ValueTask AuditAsync(string actor, string action, Guid id, string outcome, string? detail, CancellationToken cancellationToken) =>
        _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), action, "ProcessingJob", id.ToString("D"), outcome, detail), cancellationToken);

    private static void RequireOriginal(StorageVerificationResult verification, OriginalRecord original)
    {
        if (!verification.Exists || !verification.ChecksumMatches || verification.Length != original.Length)
            throw new InvalidDataException("Primary original verification failed.");
    }

    private static string ToolPath(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable)?.Trim() is { Length: > 0 } value ? value : fallback;

    private static string SafeLeaf(string name) =>
        Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } leaf ? leaf : "source.bin";

    private static string Short(string value, int max = 800) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];

    private static async Task<ToolResult> RunToolAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Unable to start transcription processing tool.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Required transcription processing tool '{file}' is unavailable.", ex);
        }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ToolResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record OriginalRecord(Guid AssetId, string ObjectKey, string OriginalFileName, long Length, string Sha256);
    private sealed record ToolResult(int ExitCode, string StdOut, string StdErr);
}
