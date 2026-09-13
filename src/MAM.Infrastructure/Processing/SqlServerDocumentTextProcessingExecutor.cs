using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Processing;

/// <summary>
/// Handles non-PDF office/text documents without sending document bytes to an external service.
/// DOCX/ODT/TXT are extracted natively; legacy DOC/RTF may use a deployment-pinned LibreOffice CLI.
/// Extracted text is persisted only through the authoritative SQL discovery index.
/// </summary>
public sealed class SqlServerDocumentTextProcessingExecutor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".docx", ".rtf", ".txt", ".odt" };
    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAuditSink _audit;
    private readonly SqlServerMediaProcessingService _processing;
    private readonly IDiscoveryService _discovery;
    private readonly string _libreOffice;

    public SqlServerDocumentTextProcessingExecutor(SqlServerConnectionFactory connections, IStorageObjectStore primary, IAuditSink audit, SqlServerMediaProcessingService processing, IDiscoveryService discovery)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _processing = processing;
        _discovery = discovery;
        _libreOffice = Environment.GetEnvironmentVariable("MAM_LIBREOFFICE_PATH")?.Trim() is { Length: > 0 } path ? path : "soffice";
    }

    public async Task<bool> CanHandleAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var original = await ReadOriginalAsync(assetId, cancellationToken);
        return original is not null && Supported.Contains(Path.GetExtension(original.OriginalFileName));
    }

    public async Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default)
    {
        if (job.State != ProcessingJobState.Leased || !string.Equals(job.LeaseOwner, workerId, StringComparison.Ordinal))
            throw new ProcessingRequestException("processing_lease_lost", "Document job is not leased by this worker.", 409);
        if (job.ProfileId is not (BuiltInProcessingProfiles.Inspect or BuiltInProcessingProfiles.PdfInline or BuiltInProcessingProfiles.OcrText))
            throw new ProcessingRequestException("document_profile_not_supported", "This document executor supports inspection and text extraction profiles only.", 409);

        var original = await ReadOriginalAsync(job.AssetId, cancellationToken)
            ?? throw new ProcessingRequestException("primary_original_not_found", "Primary original is unavailable.", 404);
        var extension = Path.GetExtension(original.OriginalFileName).ToLowerInvariant();
        if (!Supported.Contains(extension))
            throw new ProcessingRequestException("document_type_not_supported", "Document extraction does not support this file type.", 415);

        var tempRoot = Path.Combine(Path.GetTempPath(), "mam-p12-document", job.JobId.ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, Path.GetFileName(original.OriginalFileName));
        try
        {
            var before = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(before, original);
            await using (var input = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            await using (var output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);
            await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);

            var metadata = await ReadEmbeddedMetadataAsync(sourcePath, extension, cancellationToken);
            await SaveTechnicalAsync(job.AssetId, extension, metadata, cancellationToken);
            var metadataText = string.Join(' ', metadata.Select(pair => $"{pair.Key} {pair.Value}"));
            await _discovery.UpsertTextAsync(job.AssetId, DiscoverySources.Metadata, null, metadataText, original.Sha256, null, cancellationToken);

            if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
            {
                await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Running", 25, "Extracting document text.", false, cancellationToken);
                var text = await ExtractTextAsync(sourcePath, extension, tempRoot, cancellationToken);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Document text extraction produced no searchable text.");
                var segments = SplitSegments(text);
                await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Running", 90, "Indexing extracted document text.", false, cancellationToken);
                await _discovery.UpsertTextAsync(job.AssetId, DiscoverySources.Ocr, null, text, original.Sha256, segments, cancellationToken);
                await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Succeeded", 100, $"Indexed {segments.Count} document text segment(s).", true, cancellationToken);
            }

            var after = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(after, original);
            if (!string.Equals(before.ActualSha256, after.ActualSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Primary original changed during document processing.");

            await SetSucceededAsync(job.JobId, workerId, cancellationToken);
            await AuditAsync(workerId, "processing.document.completed", job.JobId, "Success", $"asset={job.AssetId:D};extension={extension};profile={job.ProfileId}", cancellationToken);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(job.JobId, workerId, ex.Message, CancellationToken.None);
            if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
            {
                try { await _discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Failed", 0, Short(ex.Message, 280), false, CancellationToken.None); } catch { }
            }
            await AuditAsync(workerId, "processing.document.failed", job.JobId, "Failed", ex.GetType().Name, CancellationToken.None);
            throw;
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    private static async Task<Dictionary<string, string>> ReadEmbeddedMetadataAsync(string sourcePath, string extension, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fileName"] = Path.GetFileName(sourcePath),
            ["extension"] = extension
        };
        if (extension == ".docx")
        {
            using var archive = ZipFile.OpenRead(sourcePath);
            var entry = archive.GetEntry("docProps/core.xml");
            if (entry is not null)
            {
                await using var stream = entry.Open();
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
                foreach (var node in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    var value = node.Value.Trim();
                    if (value.Length > 0) result[node.Name.LocalName] = value[..Math.Min(value.Length, 1000)];
                }
            }
        }
        else if (extension == ".odt")
        {
            using var archive = ZipFile.OpenRead(sourcePath);
            var entry = archive.GetEntry("meta.xml");
            if (entry is not null)
            {
                await using var stream = entry.Open();
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
                foreach (var node in document.Descendants().Where(x => x.Name.LocalName is "title" or "creator" or "creation-date" or "description" or "subject" or "keyword"))
                {
                    var value = node.Value.Trim();
                    if (value.Length > 0) result[node.Name.LocalName] = value[..Math.Min(value.Length, 1000)];
                }
            }
        }
        return result;
    }

    private async Task<string> ExtractTextAsync(string sourcePath, string extension, string tempRoot, CancellationToken cancellationToken)
    {
        if (extension == ".txt") return await File.ReadAllTextAsync(sourcePath, cancellationToken);
        if (extension == ".docx") return await ExtractXmlPackageTextAsync(sourcePath, "word/document.xml", cancellationToken);
        if (extension == ".odt") return await ExtractXmlPackageTextAsync(sourcePath, "content.xml", cancellationToken);

        var run = await RunToolAsync(_libreOffice, new[] { "--headless", "--convert-to", "txt:Text", "--outdir", tempRoot, sourcePath }, cancellationToken);
        if (run.ExitCode != 0) throw new InvalidOperationException("LibreOffice document conversion failed: " + Short(run.StdErr));
        var target = Directory.EnumerateFiles(tempRoot, "*.txt", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (target is null) throw new InvalidDataException("LibreOffice did not produce a text extraction output.");
        return await File.ReadAllTextAsync(target, cancellationToken);
    }

    private static async Task<string> ExtractXmlPackageTextAsync(string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"Document package entry '{entryName}' is missing.");
        await using var stream = entry.Open();
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var paragraphs = document.Descendants()
            .Where(node => node.Name.LocalName is "p" or "h")
            .Select(node => string.Join(' ', node.DescendantNodes().OfType<XText>().Select(text => text.Value.Trim()).Where(value => value.Length > 0)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (paragraphs.Length > 0) return string.Join(Environment.NewLine, paragraphs);
        return string.Join(' ', document.DescendantNodes().OfType<XText>().Select(text => text.Value.Trim()).Where(value => value.Length > 0));
    }

    private static IReadOnlyList<TextSegmentSnapshot> SplitSegments(string text)
    {
        var paragraphs = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0).Take(5000).ToArray();
        if (paragraphs.Length == 0) return Array.Empty<TextSegmentSnapshot>();
        return paragraphs.Select((value, index) => new TextSegmentSnapshot(index, null, null, null, value)).ToArray();
    }

    private async Task SaveTechnicalAsync(Guid assetId, string extension, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var raw = JsonSerializer.Serialize(new { format = new { format_name = extension.TrimStart('.'), tags = metadata }, streams = Array.Empty<object>() });
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            IF EXISTS(SELECT 1 FROM dbo.MamTechnicalMetadata WHERE AssetId=@AssetId)
              UPDATE dbo.MamTechnicalMetadata SET MediaType=N'Document',DurationSeconds=NULL,Width=NULL,Height=NULL,VideoCodec=NULL,AudioCodec=NULL,AudioChannels=NULL,AudioSampleRate=NULL,RawJson=@Raw,InspectedAtUtc=@Now WHERE AssetId=@AssetId;
            ELSE
              INSERT dbo.MamTechnicalMetadata(AssetId,MediaType,DurationSeconds,Width,Height,VideoCodec,AudioCodec,AudioChannels,AudioSampleRate,RawJson,InspectedAtUtc)
              VALUES(@AssetId,N'Document',NULL,NULL,NULL,NULL,NULL,NULL,NULL,@Raw,@Now);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId); command.Parameters.AddWithValue("@Raw", raw); command.Parameters.AddWithValue("@Now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<OriginalRecord?> ReadOriginalAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT AssetId,ObjectKey,OriginalFileName,Length,Sha256 FROM dbo.MamMediaOriginal WHERE AssetId=@AssetId;", connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new OriginalRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4).Trim()) : null;
    }

    private async Task SetSucceededAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = "UPDATE dbo.MamProcessingJob SET State=2,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=NULL,CompletedAtUtc=@Now,UpdatedAtUtc=@Now WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow); command.Parameters.AddWithValue("@JobId", jobId); command.Parameters.AddWithValue("@WorkerId", workerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new ProcessingRequestException("processing_lease_lost", "Document processing lease was lost before success commit.", 409);
    }

    private async Task SetFailedAsync(Guid jobId, string workerId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "UPDATE dbo.MamProcessingJob SET State=3,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL,LastHeartbeatAtUtc=NULL,LastError=@Error,UpdatedAtUtc=@Now WHERE JobId=@JobId AND State=1 AND LeaseOwner=@WorkerId;";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            command.Parameters.AddWithValue("@Error", Short(error, 1900)); command.Parameters.AddWithValue("@Now", DateTime.UtcNow); command.Parameters.AddWithValue("@JobId", jobId); command.Parameters.AddWithValue("@WorkerId", workerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { }
    }

    private ValueTask AuditAsync(string actor, string action, Guid id, string outcome, string? detail, CancellationToken cancellationToken) =>
        _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), action, "ProcessingJob", id.ToString("D"), outcome, detail), cancellationToken);

    private static void RequireOriginal(StorageVerificationResult verification, OriginalRecord original)
    {
        if (!verification.Exists || !verification.ChecksumMatches || verification.Length != original.Length) throw new InvalidDataException("Primary original verification failed.");
    }

    private static string Short(string value, int max = 800) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];

    private static async Task<ToolResult> RunToolAsync(string file, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo { FileName = file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new InvalidOperationException("Unable to start document conversion tool."); }
        catch (Win32Exception ex) { throw new InvalidOperationException($"Required document conversion tool '{file}' is unavailable.", ex); }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken); var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken); return new ToolResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record OriginalRecord(Guid AssetId, string ObjectKey, string OriginalFileName, long Length, string Sha256);
    private sealed record ToolResult(int ExitCode, string StdOut, string StdErr);
}
