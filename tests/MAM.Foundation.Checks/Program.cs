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

try
{
    var development = MamSettingsLoader.Load(args[0]);
    Check(development.Environment.SupportedCultures.Contains("ar-KW"), "Development config must include Arabic culture.");
    Check(development.Environment.SupportedCultures.Contains("en-US"), "Development config must include English culture.");
    Check(development.Storage.Primary.Id != development.Storage.Backup.Id, "Primary and Backup IDs must differ.");
    Check(development.Storage.Backup.VerifyChecksum, "Backup checksum verification must be enabled.");
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
    Check(ex.Errors.Count > 0, "Production template failure must include explicit validation errors.");
}

Check(MamSettingsValidator.Validate(new MamSettings()).Count >= 8, "Missing critical settings must fail closed with multiple explicit errors.");

try
{
    var sameTarget = MamSettingsLoader.Load(args[0]);
    sameTarget.Storage.Backup.Id = sameTarget.Storage.Primary.Id;
    Check(MamSettingsValidator.Validate(sameTarget).Any(static e => e.Contains("must be different", StringComparison.OrdinalIgnoreCase)), "Same Primary/Backup identity must be rejected.");

    var hiddenEnvironment = MamSettingsLoader.Load(args[0]);
    hiddenEnvironment.Brand.ShowEnvironmentBadge = false;
    Check(MamSettingsValidator.Validate(hiddenEnvironment).Any(static e => e.Contains("ShowEnvironmentBadge", StringComparison.OrdinalIgnoreCase)), "Non-production environment badge must be mandatory.");
}
catch (Exception ex)
{
    failures.Add($"Mutation checks could not run: {ex.Message}");
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}

Console.WriteLine("PASS: P00 foundation configuration and architecture invariants.");
return 0;
