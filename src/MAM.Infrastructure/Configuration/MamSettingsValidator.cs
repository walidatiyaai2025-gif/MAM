namespace MAM.Infrastructure.Configuration;

public static class MamSettingsValidator
{
    public static IReadOnlyList<string> Validate(MamSettings settings)
    {
        var errors = new List<string>();

        Require(settings.Environment.Name, "Environment.Name", errors);
        Require(settings.Environment.SiteCode, "Environment.SiteCode", errors);
        Require(settings.Environment.DisplayNameAr, "Environment.DisplayNameAr", errors);
        Require(settings.Environment.DisplayNameEn, "Environment.DisplayNameEn", errors);
        Require(settings.Environment.TimeZone, "Environment.TimeZone", errors);
        Require(settings.Environment.DefaultCulture, "Environment.DefaultCulture", errors);

        if (!settings.Environment.SupportedCultures.Contains("ar-KW", StringComparer.OrdinalIgnoreCase) ||
            !settings.Environment.SupportedCultures.Contains("en-US", StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Environment.SupportedCultures must include ar-KW and en-US.");
        }

        Require(settings.Server.PublicBaseUrl, "Server.PublicBaseUrl", errors);
        Require(settings.Server.ApiBasePath, "Server.ApiBasePath", errors);
        if (!Uri.TryCreate(settings.Server.PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            errors.Add("Server.PublicBaseUrl must be an absolute URL.");
        }

        var production = settings.Environment.Name.Equals("Production", StringComparison.OrdinalIgnoreCase);
        if (production && publicUri is not null && publicUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Server.PublicBaseUrl must use HTTPS in Production.");
        }

        if (production && (settings.Server.AllowedOrigins.Count == 0 || settings.Server.AllowedOrigins.Any(static origin => origin == "*")))
        {
            errors.Add("Server.AllowedOrigins must be explicitly configured in Production; wildcard is forbidden.");
        }

        if (!settings.Database.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Database.Provider must be SqlServer for the production architecture baseline.");
        }
        Require(settings.Database.ConnectionStringSecretRef, "Database.ConnectionStringSecretRef", errors);
        if (LooksLikePlaintextConnectionString(settings.Database.ConnectionStringSecretRef))
        {
            errors.Add("Database.ConnectionStringSecretRef must be a secret reference, not a plaintext connection string.");
        }

        ValidateTarget(settings.Storage.Primary, "Storage.Primary", errors);
        ValidateTarget(settings.Storage.Backup, "Storage.Backup", errors);

        if (settings.Storage.Primary.Id.Equals(settings.Storage.Backup.Id, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage.Primary.Id and Storage.Backup.Id must be different.");
        }
        if (NormalizeRoot(settings.Storage.Primary.Root).Equals(NormalizeRoot(settings.Storage.Backup.Root), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage.Primary.Root and Storage.Backup.Root must resolve to distinct targets.");
        }
        if (!settings.Storage.Backup.CopyOriginals)
        {
            errors.Add("Storage.Backup.CopyOriginals must be true.");
        }
        if (!settings.Storage.Backup.VerifyChecksum)
        {
            errors.Add("Storage.Backup.VerifyChecksum must be true before Protected state is possible.");
        }

        if (!settings.Upload.ResumeEnabled)
        {
            errors.Add("Upload.ResumeEnabled must be true.");
        }
        if (!settings.Upload.ChecksumAlgorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Upload.ChecksumAlgorithm must be SHA256.");
        }
        if (settings.Upload.ChunkSizeMB <= 0)
        {
            errors.Add("Upload.ChunkSizeMB must be greater than zero.");
        }

        Require(settings.Brand.OrganizationNameAr, "Brand.OrganizationNameAr", errors);
        Require(settings.Brand.OrganizationNameEn, "Brand.OrganizationNameEn", errors);
        Require(settings.Brand.PrimaryLogoAsset, "Brand.PrimaryLogoAsset", errors);
        if (!settings.Brand.PreserveLogoOriginalColors)
        {
            errors.Add("Brand.PreserveLogoOriginalColors must be true for the owner-supplied Diwan crest.");
        }
        if (!production && !settings.Brand.ShowEnvironmentBadge)
        {
            errors.Add("Brand.ShowEnvironmentBadge must be true outside Production.");
        }

        return errors;
    }

    private static void ValidateTarget(StorageTargetSettings target, string name, ICollection<string> errors)
    {
        Require(target.Id, $"{name}.Id", errors);
        Require(target.Type, $"{name}.Type", errors);
        Require(target.Root, $"{name}.Root", errors);
    }

    private static void Require(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{key} is required and must not contain a deployment placeholder.");
        }
    }

    private static bool LooksLikePlaintextConnectionString(string value) =>
        value.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("User Id=", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRoot(string value) => value.Trim().TrimEnd('/', '\\');
}
