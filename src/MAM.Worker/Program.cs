using System.Text.Json;
using MAM.Application.Diagnostics;
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

var resolver = new EnvironmentSecretResolver();
if (!resolver.TryResolve(settings.Database.ConnectionStringSecretRef, out var connectionString))
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P06", status = "Degraded", detail = "SQL Server secret is not resolved." }));
    Environment.ExitCode = 2;
    return;
}

var connections = new SqlServerConnectionFactory(connectionString, settings.Database.CommandTimeoutSeconds, settings.Database.EnableRetryOnFailure);
var audit = new SqlServerAuditSink(connections);
var primary = new FileSystemStorageObjectStore(settings.Storage.Primary);
var processing = new SqlServerMediaProcessingService(connections, primary, audit, settings);
var protection = new SqlServerBackupProtectionService(connections, primary, audit, settings);
var processingHealth = await processing.GetHealthAsync();
var protectionHealth = await protection.GetHealthAsync();
if (!processingHealth.IsReady || !protectionHealth.IsReady)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P06", status = "Degraded", processing = processingHealth, protection = protectionHealth }));
    Environment.ExitCode = 3;
    return;
}

var crashAfterLease = args.Contains("--crash-after-lease", StringComparer.OrdinalIgnoreCase);
var once = crashAfterLease || args.Contains("--once", StringComparer.OrdinalIgnoreCase);
var backupOnly = args.Contains("--backup-only", StringComparer.OrdinalIgnoreCase);
var processingOnly = args.Contains("--processing-only", StringComparer.OrdinalIgnoreCase);

Console.WriteLine(JsonSerializer.Serialize(new { service = "MAM.Worker", phase = "P06", status = "Ready", workerId, build, primary = primary.TargetId, backup = protectionHealth.BackupTargetId }));

do
{
    var didWork = false;

    if (!processingOnly)
    {
        var queued = await protection.QueueEligibleOriginalsAsync();
        if (queued > 0) Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-queued", count = queued, workerId }));

        var backup = await protection.LeaseNextAsync(workerId);
        if (backup is not null)
        {
            didWork = true;
            Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-leased", backup.JobId, backup.AssetId, backup.AttemptCount, workerId }));
            if (crashAfterLease)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "intentional-crash-after-backup-lease", backup.JobId, workerId }));
                Environment.ExitCode = 86;
                return;
            }

            try
            {
                var result = await protection.ExecuteAsync(backup, workerId);
                Console.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-finished", backup.JobId, backup.AssetId, result.State, result.VerifiedAtUtc, workerId }));
            }
            catch (Exception ex)
            {
                var detail = ex.Message.Length <= 800 ? ex.Message : ex.Message[..800];
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "backup-failed", backup.JobId, backup.AssetId, error = ex.GetType().Name, detail, workerId }));
                if (once) { Environment.ExitCode = 5; return; }
            }
        }
    }

    if (!backupOnly)
    {
        var job = await processing.LeaseNextAsync(workerId);
        if (job is not null)
        {
            didWork = true;
            Console.WriteLine(JsonSerializer.Serialize(new { eventName = "leased", job.JobId, job.AssetId, job.ProfileId, job.AttemptCount, workerId }));
            if (crashAfterLease)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "intentional-crash-after-lease", job.JobId, workerId }));
                Environment.ExitCode = 86;
                return;
            }

            try
            {
                await processing.ProcessAsync(job, workerId);
                Console.WriteLine(JsonSerializer.Serialize(new { eventName = "completed", job.JobId, job.AssetId, job.ProfileId, workerId }));
            }
            catch (Exception ex)
            {
                var detail = ex.Message.Length <= 800 ? ex.Message : ex.Message[..800];
                Console.Error.WriteLine(JsonSerializer.Serialize(new { eventName = "failed", job.JobId, job.AssetId, job.ProfileId, error = ex.GetType().Name, detail, workerId }));
                if (once) { Environment.ExitCode = 4; return; }
            }
        }
    }

    if (once) break;
    if (!didWork) await Task.Delay(1000);
}
while (true);
