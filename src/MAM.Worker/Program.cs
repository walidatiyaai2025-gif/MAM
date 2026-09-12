using System.Text.Json;
using MAM.Application.Diagnostics;
using MAM.Application.Processing;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Processing;
using MAM.Infrastructure.Protection;
using MAM.Infrastructure.Secrets;
using MAM.Infrastructure.Storage;

var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var settings = MamSettingsLoader.Load(configPath);
var build = BuildInfo.Current.WithEnvironment(settings.Environment.Name);
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
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P09", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), detail = "SQL Server secret is not resolved." }));
    Environment.ExitCode = 2;
    return;
}

var connections = new SqlServerConnectionFactory(connectionString, settings.Database.CommandTimeoutSeconds, settings.Database.EnableRetryOnFailure);
var audit = new SqlServerAuditSink(connections);
var primary = new FileSystemStorageObjectStore(settings.Storage.Primary);
var processing = new SqlServerMediaProcessingService(connections, primary, audit, settings);
var ocr = new SqlServerOcrProcessingExecutor(connections, primary, audit, settings, processing);
var protection = new SqlServerBackupProtectionService(connections, primary, audit, settings);
var processingHealth = await processing.GetHealthAsync();
var protectionHealth = await protection.GetHealthAsync();

if (!backupOnly && !processingHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P09", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), processing = processingHealth }));
    Environment.ExitCode = 3;
    return;
}

if (!processingOnly && !protectionHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P09", status = "Degraded", correlationId = Guid.NewGuid().ToString("N"), protection = protectionHealth, action = "backup-jobs-remain-fail-closed-and-retryable" }));
}

Console.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P09", status = protectionHealth.IsReady ? "Ready" : "Degraded", correlationId = Guid.NewGuid().ToString("N"), workerId, build, primary = primary.TargetId, backup = protectionHealth.BackupTargetId }));

do
{
    var didWork = false;

    // Preserve the established P04 contract: media processing has priority unless the caller explicitly asks for backup-only work.
    // This keeps existing --once/--crash-after-lease semantics deterministic while backup work remains independently addressable.
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
                if (string.Equals(job.ProfileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase))
                    await ocr.ProcessAsync(job, workerId);
                else
                    await processing.ProcessAsync(job, workerId);
                Console.WriteLine(JsonSerializer.Serialize(new { eventName = "completed", correlationId, job.JobId, job.AssetId, job.ProfileId, workerId }));
            }
            catch (Exception ex)
            {
                var detail = ex.Message.Length <= 800 ? ex.Message : ex.Message[..800];
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
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "intentional-crash-after-backup-lease", correlationId, backup.JobId, workerId }));
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
                var detail = ex.Message.Length <= 800 ? ex.Message : ex.Message[..800];
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-failed", correlationId, backup.JobId, backup.AssetId, error = ex.GetType().Name, detail, workerId }));
                if (once) { Environment.ExitCode = 5; return; }
            }
        }
    }

    if (once) break;
    if (!didWork) await Task.Delay(1000);
}
while (true);
