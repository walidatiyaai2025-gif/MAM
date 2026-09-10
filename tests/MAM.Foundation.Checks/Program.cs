using MAM.Application.Diagnostics;
using MAM.Infrastructure.Configuration;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MAM.Foundation.Checks <development-config> <production-template>");
    return 2;
}

var failures = new List<string>();
void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

var buildInfo = BuildInfo.Current;
Check(!string.IsNullOrWhiteSpace(buildInfo.Version), "Build version must be surfaced.");
Check(!string.IsNullOrWhiteSpace(buildInfo.CommitSha), "Build commit SHA field must be surfaced.");
Check(!string.IsNullOrWhiteSpace(buildInfo.BuildNumber), "Build number field must be surfaced.");
Check(!string.IsNullOrWhiteSpace(buildInfo.BuildTimestampUtc), "Build timestamp field must be surfaced.");
Check(!string.IsNullOrWhiteSpace(buildInfo.EnvironmentName), "Environment name must be surfaced.");
if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
{
    Check(buildInfo.CommitSha != "local", "CI build must stamp the commit SHA into assembly metadata.");
    Check(buildInfo.BuildNumber != "local", "CI build must stamp the build number into assembly metadata.");
    Check(buildInfo.BuildTimestampUtc != "not-stamped", "CI build must stamp the UTC build timestamp into assembly metadata.");
}

try
{
    var webProject = File.ReadAllText("src/MAM.Web/MAM.Web.csproj");
    var desktopProject = File.ReadAllText("src/MAM.Desktop/MAM.Desktop.csproj");
    var webProgram = File.ReadAllText("src/MAM.Web/Program.cs");
    var desktopXaml = File.ReadAllText("src/MAM.Desktop/MainWindow.xaml");

    Check(!webProject.Contains("MAM.Infrastructure", StringComparison.Ordinal), "Web client must not reference the server-side Infrastructure project.");
    Check(!desktopProject.Contains("MAM.Infrastructure", StringComparison.Ordinal), "Desktop client must not reference the server-side Infrastructure project.");
    Check(!webProgram.Contains("MamSettingsLoader", StringComparison.Ordinal), "Web client must not load server database/storage configuration.");
    Check(desktopXaml.Contains("BuildIdentityText", StringComparison.Ordinal), "Desktop deployable must surface stamped build identity.");
}
catch (Exception ex)
{
    failures.Add($"Client/server architecture checks could not run: {ex.Message}");
}

try
{
    var development = MamSettingsLoader.Load(args[0]);
    Check(development.Environment.SupportedCultures.Contains("ar-KW"), "Development config must include Arabic culture.");
    Check(development.Environment.SupportedCultures.Contains("en-US"), "Development config must include English culture.");
    Check(development.Storage.Primary.Id != development.Storage.Backup.Id, "Primary and Backup IDs must differ.");
    Check(development.Storage.Backup.VerifyChecksum, "Backup checksum verification must be enabled.");
    Check(development.Upload.ChecksumAlgorithm == "SHA256", "Upload checksum algorithm must be SHA256.");
    Check(development.Server.Health.EndpointEnabled, "Health endpoint must be enabled.");
    Check(development.Jobs.StaleJobRecoveryEnabled, "Stale job recovery must be enabled.");
    Check(development.Search.FacetsEnabled, "Search facets must be enabled.");
    Check(development.Retention.PurgePrimaryAndBackupTogether, "Deletion workflow must preserve Primary/Backup consistency.");
    Check(!development.Logging.IncludeSensitiveMetadata, "Sensitive metadata logging must remain disabled.");
    Check(development.Brand.PreserveLogoOriginalColors, "Diwan crest colors must be preserved.");
}
catch (Exception ex)
{
    failures.Add($"Development config should be valid: {ex.Message}");
}

try
{
    _ = MamSettingsLoader.Load(args[1]);
    failures.Add("Production template must fail validation until deployment placeholders are replaced.");
}
catch (MamConfigurationException ex)
{
    Check(ex.Errors.Count >= 5, "Production template failure must include multiple explicit deployment errors.");
}

Check(MamSettingsValidator.Validate(new MamSettings()).Count >= 20, "Missing critical settings must fail closed with comprehensive explicit errors.");

try
{
    var sameTarget = MamSettingsLoader.Load(args[0]);
    sameTarget.Storage.Backup.Id = sameTarget.Storage.Primary.Id;
    Check(MamSettingsValidator.Validate(sameTarget).Any(static e => e.Contains("must be different", StringComparison.OrdinalIgnoreCase)), "Same Primary/Backup identity must be rejected.");

    var hiddenEnvironment = MamSettingsLoader.Load(args[0]);
    hiddenEnvironment.Brand.ShowEnvironmentBadge = false;
    Check(MamSettingsValidator.Validate(hiddenEnvironment).Any(static e => e.Contains("ShowEnvironmentBadge", StringComparison.OrdinalIgnoreCase)), "Non-production environment badge must be mandatory.");

    var incompletePrimary = MamSettingsLoader.Load(args[0]);
    incompletePrimary.Storage.Primary.OriginalsPrefix = string.Empty;
    Check(MamSettingsValidator.Validate(incompletePrimary).Any(static e => e.Contains("OriginalsPrefix", StringComparison.OrdinalIgnoreCase)), "Missing Primary originals prefix must be rejected.");

    var unprotectedBackup = MamSettingsLoader.Load(args[0]);
    unprotectedBackup.Storage.Backup.VerifyChecksum = false;
    Check(MamSettingsValidator.Validate(unprotectedBackup).Any(static e => e.Contains("VerifyChecksum", StringComparison.OrdinalIgnoreCase)), "Backup without checksum verification must be rejected.");

    var unsafeLogging = MamSettingsLoader.Load(args[0]);
    unsafeLogging.Logging.IncludeSensitiveMetadata = true;
    Check(MamSettingsValidator.Validate(unsafeLogging).Any(static e => e.Contains("IncludeSensitiveMetadata", StringComparison.OrdinalIgnoreCase)), "Sensitive metadata logging must be rejected.");

    var brokenJobs = MamSettingsLoader.Load(args[0]);
    brokenJobs.Jobs.HeartbeatSeconds = brokenJobs.Jobs.LeaseSeconds;
    Check(MamSettingsValidator.Validate(brokenJobs).Any(static e => e.Contains("HeartbeatSeconds", StringComparison.OrdinalIgnoreCase)), "Job heartbeat must be shorter than the durable lease.");
}
catch (Exception ex)
{
    failures.Add($"Mutation checks could not run: {ex.Message}");
}

var tempConfig = Path.GetTempFileName();
try
{
    var json = File.ReadAllText(args[0]);
    json = json.Replace("\"Environment\": {", "\"UndocumentedDangerousSwitch\": true,\n  \"Environment\": {", StringComparison.Ordinal);
    File.WriteAllText(tempConfig, json);

    try
    {
        _ = MamSettingsLoader.Load(tempConfig);
        failures.Add("Undocumented configuration keys must be rejected instead of silently ignored.");
    }
    catch (MamConfigurationException ex)
    {
        Check(ex.Errors.Any(static e => e.Contains("schema", StringComparison.OrdinalIgnoreCase)), "Unknown-key failure must be reported as a configuration schema error.");
    }
}
finally
{
    File.Delete(tempConfig);
}

var nullConfig = Path.GetTempFileName();
try
{
    var json = File.ReadAllText(args[0])
        .Replace("\"Storage\": {", "\"Storage\": null,\n  \"IgnoredStorageReplacement\": {", StringComparison.Ordinal);
    var start = json.IndexOf("  \"IgnoredStorageReplacement\": {", StringComparison.Ordinal);
    if (start >= 0)
    {
        var uploadStart = json.IndexOf("  \"Upload\": {", start, StringComparison.Ordinal);
        if (uploadStart >= 0)
        {
            json = json[..start] + json[uploadStart..];
        }
    }
    File.WriteAllText(nullConfig, json);

    try
    {
        _ = MamSettingsLoader.Load(nullConfig);
        failures.Add("Null critical configuration sections must fail validation.");
    }
    catch (MamConfigurationException ex)
    {
        Check(ex.Errors.Any(static e => e.Contains("Storage.Primary", StringComparison.OrdinalIgnoreCase)), "Null Storage must produce explicit storage validation errors rather than a runtime null-reference failure.");
    }
}
finally
{
    File.Delete(nullConfig);
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}

Console.WriteLine("PASS: P00 foundation configuration, build metadata, client/server boundaries, security and centralized architecture invariants.");
return 0;
