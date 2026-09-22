using System.Text.Json;
using System.Text.RegularExpressions;
using MAM.Application.Diagnostics;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Discovery;
using MAM.Infrastructure.Processing;
using MAM.Infrastructure.Protection;
using MAM.Infrastructure.Secrets;
using MAM.Infrastructure.Storage;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var settings = MamSettingsLoader.Load(configPath);
var build = BuildInfo.Current.WithEnvironment(settings.Environment.Name);
var runtimeInspector = new RuntimeInspectorLog("MAM.Worker", build);
runtimeInspector.Write(new RuntimeDiagnosticEvent(
    "Information", "process-start",
    $"MAM.Worker started. Runtime inspector log root: {runtimeInspector.RootPath}",
    Metadata: new Dictionary<string, string?> { ["logRoot"] = runtimeInspector.RootPath }));
var workerId = Environment.GetEnvironmentVariable("MAM_WORKER_ID")?.Trim();
if (string.IsNullOrWhiteSpace(workerId)) workerId = $"{Environment.MachineName}-{Environment.ProcessId}";

var legacyCrashAfterProcessingLease = args.Contains("--crash-after-lease", StringComparer.OrdinalIgnoreCase);
var crashAfterBackupLease = args.Contains("--crash-after-backup-lease", StringComparer.OrdinalIgnoreCase);
var backupOnly = args.Contains("--backup-only", StringComparer.OrdinalIgnoreCase);
var processingOnly = args.Contains("--processing-only", StringComparer.OrdinalIgnoreCase);
var once = legacyCrashAfterProcessingLease || crashAfterBackupLease || args.Contains("--once", StringComparer.OrdinalIgnoreCase);

var resolver = new EnvironmentSecretResolver();
if (!resolver.TryResolve(settings.Database.ConnectionStringSecretRef, out var connectionString))
{
    var correlationId = Guid.NewGuid().ToString("N");
    runtimeInspector.Write(new RuntimeDiagnosticEvent(
        "Error", "worker-startup-database-secret",
        "SQL Server secret is not resolved.",
        CorrelationId: correlationId));
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P12", status = "Degraded", correlationId, detail = "SQL Server secret is not resolved." }));
    Environment.ExitCode = 2;
    return;
}

var connections = new SqlServerConnectionFactory(connectionString, settings.Database.CommandTimeoutSeconds, settings.Database.EnableRetryOnFailure);
var audit = new SqlServerAuditSink(connections);
var primary = new FileSystemStorageObjectStore(settings.Storage.Primary);
var discovery = new SqlServerDiscoveryService(connections, audit);
var processing = new SqlServerMediaProcessingService(connections, primary, audit, settings);
var visualProvider = new LocalImageVisualEmbeddingProvider();
var visual = new SqlServerVisualSearchService(connections, primary, visualProvider, settings);
var visualProcessing = new SqlServerVisualProcessingExecutor(connections, primary, audit, processing, discovery, visual);
var ocr = new SqlServerOcrProcessingExecutor(connections, primary, audit, settings, processing);
var transcript = new SqlServerTranscriptProcessingExecutor(connections, primary, audit, processing, discovery);
var document = new SqlServerDocumentTextProcessingExecutor(connections, primary, audit, processing, discovery);
var protection = new SqlServerBackupProtectionService(connections, primary, audit, settings);
var processingHealth = await processing.GetHealthAsync();
var protectionHealth = await protection.GetHealthAsync();
var discoveryHealth = await discovery.GetHealthAsync();
var visualHealth = await visual.GetHealthAsync();

if (!backupOnly && !processingHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P12", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), processing = processingHealth }));
    Environment.ExitCode = 3;
    return;
}

if (!backupOnly && !discoveryHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P12", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), discovery = discoveryHealth }));
    Environment.ExitCode = 6;
    return;
}

if (!processingOnly && !protectionHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P12", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), protection = protectionHealth, action = "backup-jobs-remain-fail-closed-and-retryable" }));
}

Console.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P12", status = protectionHealth.IsReady ? "Ready" : "Degraded", correlationId = Guid.NewGuid().ToString("N"), workerId, build, primary = primary.TargetId, backup = protectionHealth.BackupTargetId, discovery = discoveryHealth.Provider, visual = new { visualHealth.IsReady, visualHealth.Provider, visualHealth.ModelId, visualHealth.ModelVersion, visualHealth.Dimensions }, processOutputEncoding = Console.OutputEncoding.WebName }));

do
{
    var didWork = false;

    if (!backupOnly)
    {
        var job = await processing.LeaseNextAsync(workerId);
        if (job is not null)
        {
            didWork = true;
            var correlationId = $"processing-{job.JobId:N}";
            Console.WriteLine(JsonSerializer.Serialize(new { eventName = "leased", correlationId, job.JobId, job.AssetId, job.ProfileId, job.AttemptCount, workerId }));
            if (legacyCrashAfterProcessingLease)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "intentional-crash-after-lease", correlationId, job.JobId, workerId }));
                Environment.ExitCode = 86;
                return;
            }

            try
            {
                var documentProfile = string.Equals(job.ProfileId, BuiltInProcessingProfiles.Inspect, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(job.ProfileId, BuiltInProcessingProfiles.PdfInline, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.VisualSegments, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(job.ProfileId, BuiltInProcessingProfiles.VisualIndex, StringComparison.OrdinalIgnoreCase))
                {
                    await visualProcessing.ProcessAsync(job, workerId);
                }
                else if (documentProfile && await document.CanHandleAsync(job.AssetId))
                {
                    await document.ProcessAsync(job, workerId);
                }
                else if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
                {
                    await discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Running", 10, "Running Arabic/English OCR.", false);
                    await ocr.ProcessAsync(job, workerId);
                    await discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Running", 90, "Indexing OCR text and page boundaries.", false);
                    await IndexOcrDerivativeAsync(job.AssetId, processing, discovery);
                    await discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Succeeded", 100, "OCR text is indexed and searchable.", true);
                }
                else if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.TranscriptText, StringComparison.OrdinalIgnoreCase))
                {
                    await transcript.ProcessAsync(job, workerId);
                    await QueueOrRetryAsync(job.AssetId, BuiltInProcessingProfiles.VisualSegments, processing, workerId);
                }
                else
                {
                    await processing.ProcessAsync(job, workerId);
                    if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.Inspect, StringComparison.OrdinalIgnoreCase))
                        await IndexDetectedMetadataAsync(job.AssetId, processing, discovery);
                    if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.ImagePreview, StringComparison.OrdinalIgnoreCase))
                        await QueueOrRetryAsync(job.AssetId, BuiltInProcessingProfiles.VisualIndex, processing, workerId);
                }
                Console.WriteLine(JsonSerializer.Serialize(new { eventName = "completed", correlationId, job.JobId, job.AssetId, job.ProfileId, workerId }));
            }
            catch (Exception ex)
            {
                runtimeInspector.Write(new RuntimeDiagnosticEvent(
                    "Error",
                    "processing-job-failed",
                    ex.Message,
                    ex.GetType().FullName,
                    ex.ToString(),
                    correlationId,
                    Metadata: new Dictionary<string, string?>
                    {
                        ["jobId"] = job.JobId.ToString("D"),
                        ["assetId"] = job.AssetId.ToString("D"),
                        ["profileId"] = job.ProfileId,
                        ["workerId"] = workerId
                    }));
                if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
                {
                    try { await discovery.SetExtractionStatusAsync(job.AssetId, DiscoverySources.Ocr, "Failed", 0, Short(ex.Message, 280), false); } catch { }
                }
                var detail = Short(ex.Message, 800);
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "failed", correlationId, job.JobId, job.AssetId, job.ProfileId, error = ex.GetType().Name, detail, workerId }));
                if (once) { Environment.ExitCode = 4; return; }
            }

            if (once) break;
        }
    }

    if (!processingOnly)
    {
        var queued = await protection.QueueEligibleOriginalsAsync();
        if (queued > 0) Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-queued", correlationId = Guid.NewGuid().ToString("N"), count = queued, workerId }));

        var backup = await protection.LeaseNextAsync(workerId);
        if (backup is not null)
        {
            didWork = true;
            var correlationId = $"backup-{backup.JobId:N}";
            Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-leased", correlationId, backup.JobId, backup.AssetId, backup.AttemptCount, workerId }));
            if (crashAfterBackupLease)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "intentional-crash-after-lease", correlationId, backup.JobId, workerId }));
                Environment.ExitCode = 86;
                return;
            }

            try
            {
                var result = await protection.ExecuteAsync(backup, workerId);
                Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-finished", correlationId, backup.JobId, backup.AssetId, result.State, result.VerifiedAtUtc, workerId }));
                if (once && result.State != MAM.Application.Protection.BackupProtectionState.Protected)
                {
                    Environment.ExitCode = 5;
                    return;
                }
            }
            catch (Exception ex)
            {
                runtimeInspector.Write(new RuntimeDiagnosticEvent(
                    "Error",
                    "backup-job-failed",
                    ex.Message,
                    ex.GetType().FullName,
                    ex.ToString(),
                    correlationId,
                    Metadata: new Dictionary<string, string?>
                    {
                        ["jobId"] = backup.JobId.ToString("D"),
                        ["assetId"] = backup.AssetId.ToString("D"),
                        ["workerId"] = workerId
                    }));
                var detail = Short(ex.Message, 800);
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-failed", correlationId, backup.JobId, backup.AssetId, error = ex.GetType().Name, detail, workerId }));
                if (once) { Environment.ExitCode = 5; return; }
            }
        }
    }

    if (once) break;
    if (!didWork) await Task.Delay(1000);
}
while (true);

static async Task QueueOrRetryAsync(Guid assetId, string profileId, IMediaProcessingService processing, string actor)
{
    var queued = await processing.EnqueueAsync(assetId, profileId, actor);
    if (queued.State == ProcessingJobState.Failed)
        await processing.RetryAsync(queued.JobId, actor);
}

static async Task IndexOcrDerivativeAsync(Guid assetId, IMediaProcessingService processing, IDiscoveryService discovery)
{
    var derivative = (await processing.ListDerivativesAsync(assetId))
        .Where(item => string.Equals(item.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => item.CreatedAtUtc)
        .FirstOrDefault() ?? throw new InvalidDataException("OCR derivative is missing after successful OCR processing.");

    var payload = await processing.OpenDerivativeAsync(assetId, derivative.DerivativeId)
        ?? throw new InvalidDataException("OCR derivative cannot be opened for indexing.");
    using var reader = new StreamReader(payload.Content, detectEncodingFromByteOrderMarks: true);
    var text = await reader.ReadToEndAsync();
    var segments = ParseOcrSegments(text);
    await discovery.UpsertTextAsync(assetId, DiscoverySources.Ocr, "ara+eng", text, derivative.Sha256, segments);
}

static async Task IndexDetectedMetadataAsync(Guid assetId, IMediaProcessingService processing, IDiscoveryService discovery)
{
    var technical = await processing.GetTechnicalMetadataAsync(assetId);
    if (technical is null) return;

    var parts = new List<string>
    {
        $"mediaType {technical.MediaType}",
        technical.DurationSeconds is null ? string.Empty : $"duration {technical.DurationSeconds:0.###}",
        technical.Width is null || technical.Height is null ? string.Empty : $"dimensions {technical.Width}x{technical.Height}",
        string.IsNullOrWhiteSpace(technical.VideoCodec) ? string.Empty : $"videoCodec {technical.VideoCodec}",
        string.IsNullOrWhiteSpace(technical.AudioCodec) ? string.Empty : $"audioCodec {technical.AudioCodec}"
    };

    try
    {
        using var json = JsonDocument.Parse(technical.RawJson);
        if (json.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var formatTags) && formatTags.ValueKind == JsonValueKind.Object)
            AddMetadataTags(formatTags, parts);
        if (json.RootElement.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
                if (stream.TryGetProperty("tags", out var streamTags) && streamTags.ValueKind == JsonValueKind.Object)
                    AddMetadataTags(streamTags, parts);
        }
    }
    catch (JsonException)
    {
        // The authoritative technical snapshot remains available even if a vendor emitted malformed optional tags.
    }

    var text = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    if (text.Length > 0)
        await discovery.UpsertTextAsync(assetId, DiscoverySources.Metadata, null, text, null, null);
}

static void AddMetadataTags(JsonElement tags, ICollection<string> output)
{
    foreach (var property in tags.EnumerateObject().Take(100))
    {
        var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        if (string.IsNullOrWhiteSpace(value)) continue;
        value = value.Trim();
        if (value.Length > 1000) value = value[..1000];
        output.Add($"{property.Name} {value}");
    }
}

static IReadOnlyList<TextSegmentSnapshot> ParseOcrSegments(string text)
{
    var result = new List<TextSegmentSnapshot>();
    var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    var matches = Regex.Matches(normalized, @"(?m)^--- Page (?<page>\d+) ---\s*$", RegexOptions.CultureInvariant);
    if (matches.Count == 0)
    {
        if (!string.IsNullOrWhiteSpace(normalized)) result.Add(new TextSegmentSnapshot(0, null, null, 1, normalized.Trim()));
        return result;
    }
    for (var i = 0; i < matches.Count; i++)
    {
        var page = int.Parse(matches[i].Groups["page"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var start = matches[i].Index + matches[i].Length;
        var end = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;
        var pageText = normalized[start..end].Trim();
        if (pageText.Length > 0) result.Add(new TextSegmentSnapshot(result.Count, null, null, page, pageText));
    }
    return result;
}

static string Short(string value, int max) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];