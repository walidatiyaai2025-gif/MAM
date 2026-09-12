using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MAM.Application.Auditing;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Processing;

/// <summary>
/// Executes the OCR processing profile inside the server worker boundary.
/// Originals remain immutable in Primary Storage; OCR output is persisted as a
/// checksum-verified derivative through the same authoritative storage contract.
/// </summary>
public sealed class SqlServerOcrProcessingExecutor
{
    private const string OcrLanguages = "ara+eng";
    private const int MaxPdfPages = 250;

    private readonly SqlServerConnectionFactory _connections;
    private readonly IStorageObjectStore _primary;
    private readonly IAuditSink _audit;
    private readonly MamSettings _settings;
    private readonly SqlServerMediaProcessingService _processing;
    private readonly string _tesseract;
    private readonly string _pdftoppm;

    public SqlServerOcrProcessingExecutor(
        SqlServerConnectionFactory connections,
        IStorageObjectStore primary,
        IAuditSink audit,
        MamSettings settings,
        SqlServerMediaProcessingService processing)
    {
        _connections = connections;
        _primary = primary;
        _audit = audit;
        _settings = settings;
        _processing = processing;
        _tesseract = ToolPath("MAM_TESSERACT_PATH", "tesseract");
        _pdftoppm = ToolPath("MAM_PDFTOPPM_PATH", "pdftoppm");
    }

    public async Task ProcessAsync(ProcessingJobSnapshot job, string workerId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OCR executor received a non-OCR processing profile.", nameof(job));
        if (job.State != ProcessingJobState.Leased || !string.Equals(job.LeaseOwner, workerId, StringComparison.Ordinal))
            throw new ProcessingRequestException("processing_lease_lost", "OCR job is not leased by this worker.", 409);

        var original = await ReadOriginalAsync(job.AssetId, cancellationToken)
            ?? throw new ProcessingRequestException("primary_original_not_found", "Primary original is unavailable.", 404);
        var extension = Path.GetExtension(original.OriginalFileName);
        if (!IsOcrImage(extension) && !string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ProcessingRequestException("ocr_media_type_not_supported", "OCR supports JPEG, PNG, TIFF, BMP and PDF originals.", 415);

        var tempRoot = Path.Combine(Path.GetTempPath(), "mam-p12-ocr", job.JobId.ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, SafeLeaf(original.OriginalFileName));

        try
        {
            var before = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(before, original);
            await using (var input = await _primary.OpenReadAsync(original.ObjectKey, cancellationToken))
            await using (var output = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);

            await _processing.HeartbeatAsync(job.JobId, workerId, cancellationToken);
            var text = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
                ? await OcrPdfAsync(sourcePath, tempRoot, job.JobId, workerId, cancellationToken)
                : await OcrImageAsync(sourcePath, cancellationToken);

            var normalized = NormalizeText(text);
            var outputPath = Path.Combine(tempRoot, "ocr.txt");
            await File.WriteAllTextAsync(outputPath, normalized, new UTF8Encoding(false), cancellationToken);
            var length = new FileInfo(outputPath).Length;
            var sha256 = await FileShaAsync(outputPath, cancellationToken);
            var profile = BuiltInProcessingProfiles.Find(BuiltInProcessingProfiles.OcrText)
                ?? throw new InvalidOperationException("OCR processing profile is unavailable.");
            var objectKey = DerivativeKey(job.AssetId, profile, original.Sha256);

            var stored = await _primary.VerifyAsync(objectKey, sha256, cancellationToken);
            if (!stored.Exists)
            {
                await using var derivativeSource = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await _primary.WriteAsync(objectKey, derivativeSource, sha256, cancellationToken);
                stored = await _primary.VerifyAsync(objectKey, sha256, cancellationToken);
            }
            if (!stored.Exists || !stored.ChecksumMatches || stored.Length != length)
                throw new InvalidDataException("OCR derivative verification failed.");

            var derivative = new DerivativeSnapshot(
                DerivativeId(job.AssetId, profile, original.Sha256),
                job.AssetId,
                profile.Id,
                profile.Version,
                objectKey,
                profile.ContentType!,
                length,
                sha256,
                DateTimeOffset.UtcNow);
            await SaveDerivativeAsync(derivative, cancellationToken);

            var after = await _primary.VerifyAsync(original.ObjectKey, original.Sha256, cancellationToken);
            RequireOriginal(after, original);
            if (!string.Equals(before.ActualSha256, after.ActualSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Primary original changed during OCR processing.");

            await SetSucceededAsync(job.JobId, workerId, cancellationToken);
            await AuditAsync(workerId, "processing.job.completed", job.JobId, "Success",
                $"asset={job.AssetId:D};profile={profile.Id};languages={OcrLanguages};derivative={derivative.DerivativeId:D};sha256={derivative.Sha256}", cancellationToken);
        }
        catch (Exception ex)
        {
            await SetFailedAsync(job.JobId, workerId, ex.Message, CancellationToken.None);
            await AuditAsync(workerId, "processing.job.failed", job.JobId, "Failed", $"ocr:{ex.GetType().Name}", CancellationToken.None);
            throw;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private async Task<string> OcrPdfAsync(string sourcePath, string tempRoot, Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        var pagePrefix = Path.Combine(tempRoot, "ocr-page");
        var render = await RunToolAsync(_pdftoppm, new[] { "-png", "-r", "220", sourcePath, pagePrefix }, cancellationToken);
        if (render.ExitCode != 0)
            throw new InvalidOperationException("PDF rasterization for OCR failed: " + Short(render.StdErr));

        var pages = Directory.EnumerateFiles(tempRoot, "ocr-page-*.png")
            .OrderBy(PageNumber)
            .ToArray();
        if (pages.Length == 0) throw new InvalidDataException("PDF rasterization produced no OCR pages.");
        if (pages.Length > MaxPdfPages)
            throw new ProcessingRequestException("ocr_pdf_page_limit_exceeded", $"OCR PDF page limit is {MaxPdfPages} pages.", 413);

        var text = new StringBuilder();
        for (var i = 0; i < pages.Length; i++)
        {
            await _processing.HeartbeatAsync(jobId, workerId, cancellationToken);
            var pageText = await OcrImageAsync(pages[i], cancellationToken);
            if (i > 0) text.AppendLine().AppendLine();
            text.Append("--- Page ").Append(i + 1).AppendLine(" ---");
            text.Append(pageText);
        }
        return text.ToString();
    }

    private async Task<string> OcrImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var run = await RunToolAsync(_tesseract, new[] { imagePath, "stdout", "-l", OcrLanguages, "--psm", "3" }, cancellationToken);
        if (run.ExitCode != 0)
            throw new InvalidOperationException("Tesseract OCR failed: " + Short(run.StdErr));
        return run.StdOut;
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

    private async Task SaveDerivativeAsync(DerivativeSnapshot derivative, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.MamMediaDerivative WHERE AssetId=@AssetId AND ProfileId=@ProfileId AND ProfileVersion=@Version)
                INSERT dbo.MamMediaDerivative(DerivativeId,AssetId,ProfileId,ProfileVersion,ObjectKey,ContentType,Length,Sha256,CreatedAtUtc)
                VALUES(@DerivativeId,@AssetId,@ProfileId,@Version,@ObjectKey,@ContentType,@Length,@Sha256,@CreatedAtUtc);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@DerivativeId", derivative.DerivativeId);
        command.Parameters.AddWithValue("@AssetId", derivative.AssetId);
        command.Parameters.AddWithValue("@ProfileId", derivative.ProfileId);
        command.Parameters.AddWithValue("@Version", derivative.ProfileVersion);
        command.Parameters.AddWithValue("@ObjectKey", derivative.ObjectKey);
        command.Parameters.AddWithValue("@ContentType", derivative.ContentType);
        command.Parameters.AddWithValue("@Length", derivative.Length);
        command.Parameters.AddWithValue("@Sha256", derivative.Sha256);
        command.Parameters.AddWithValue("@CreatedAtUtc", derivative.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            throw new ProcessingRequestException("processing_lease_lost", "OCR processing lease was lost before success commit.", 409);
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

    private string DerivativeKey(Guid assetId, ProcessingProfileDescriptor profile, string sourceSha) =>
        string.Join('/', _settings.Storage.Primary.DerivativesPrefix.Trim('/'), assetId.ToString("D"), $"{profile.Id}-v{profile.Version}", sourceSha[..16] + profile.OutputExtension);

    private static Guid DerivativeId(Guid assetId, ProcessingProfileDescriptor profile, string sourceSha) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{assetId:D}|{profile.Id}|{profile.Version}|{sourceSha}"))[..16]);

    private static void RequireOriginal(StorageVerificationResult verification, OriginalRecord original)
    {
        if (!verification.Exists || !verification.ChecksumMatches || verification.Length != original.Length)
            throw new InvalidDataException("Primary original verification failed.");
    }

    private static bool IsOcrImage(string extension) => new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" }
        .Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static int PageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name[(separator + 1)..], out var page) ? page : int.MaxValue;
    }

    private static string NormalizeText(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();
        return normalized.Length == 0 ? string.Empty : normalized + "\n";
    }

    private static string ToolPath(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable)?.Trim() is { Length: > 0 } value ? value : fallback;

    private static string SafeLeaf(string name) =>
        Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } leaf ? leaf : "source.bin";

    private static string Short(string value, int max = 800) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];

    private static async Task<string> FileShaAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }

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
            if (!process.Start()) throw new InvalidOperationException("Unable to start OCR processing tool.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Required OCR processing tool '{file}' is unavailable.", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ToolResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record OriginalRecord(Guid AssetId, string ObjectKey, string OriginalFileName, long Length, string Sha256);
    private sealed record ToolResult(int ExitCode, string StdOut, string StdErr);
}
