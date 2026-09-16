namespace MAM.Infrastructure.Configuration;

internal static class DemoSettingsValidator
{
    public static IReadOnlyList<string> Validate(MamSettings settings)
    {
        var errors = new List<string>();
        if (!string.Equals(settings.Environment.Name, "Demo", StringComparison.OrdinalIgnoreCase)) errors.Add("Demo configuration requires Environment.Name=Demo.");
        if (!string.Equals(settings.Database.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase)) errors.Add("Demo configuration requires Database.Provider=Sqlite.");
        Require(settings.Database.SqlitePath, "Database.SqlitePath", errors);
        if (!string.Equals(settings.Auth.Mode, "Local", StringComparison.OrdinalIgnoreCase)) errors.Add("Demo configuration requires Auth.Mode=Local.");
        if (!Uri.TryCreate(settings.Server.PublicBaseUrl, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttp)
            errors.Add("Demo Server.PublicBaseUrl must be an absolute HTTP URL.");
        if (!string.Equals(settings.Server.ApiBasePath, "/api", StringComparison.Ordinal)) errors.Add("Demo Server.ApiBasePath must be /api.");
        if (!settings.Server.Health.EndpointEnabled) errors.Add("Demo health endpoint must be enabled.");
        if (settings.Server.MaxRequestBodyMB <= 0 || settings.Server.RequestTimeoutSeconds <= 0) errors.Add("Demo server limits must be positive.");
        ValidateStorage(settings.Storage.Primary, "Storage.Primary", errors);
        ValidateStorage(settings.Storage.Backup, "Storage.Backup", errors);
        if (string.Equals(Path.GetFullPath(settings.Storage.Primary.Root), Path.GetFullPath(settings.Storage.Backup.Root), StringComparison.OrdinalIgnoreCase))
            errors.Add("Demo Primary and Backup roots must be distinct.");
        if (!settings.Storage.Backup.CopyOriginals || !settings.Storage.Backup.VerifyChecksum) errors.Add("Demo backup must copy originals and verify checksums.");
        if (!settings.Upload.WebEnabled || !settings.Upload.ResumeEnabled) errors.Add("Demo Web upload and resume must be enabled.");
        if (!string.Equals(settings.Upload.ChecksumAlgorithm, "SHA256", StringComparison.OrdinalIgnoreCase)) errors.Add("Demo upload checksum must be SHA256.");
        if (settings.Upload.ChunkSizeMB <= 0 || settings.Upload.MaxFileSizeGB <= 0 || settings.Upload.SessionExpiryHours <= 0) errors.Add("Demo upload limits must be positive.");
        if (settings.Upload.AllowedExtensions is null || settings.Upload.AllowedExtensions.Count == 0) errors.Add("Demo upload extensions must be configured.");
        if (settings.Search.DefaultPageSize <= 0 || settings.Search.MaxPageSize < settings.Search.DefaultPageSize) errors.Add("Demo search page sizes are invalid.");
        if (!settings.Search.FacetsEnabled) errors.Add("Demo search facets must be enabled.");
        if (!settings.Retention.SoftDeleteEnabled || !settings.Retention.RequireReasonForDelete || !settings.Retention.PurgePrimaryAndBackupTogether) errors.Add("Demo retention safety controls must remain enabled.");
        if (!settings.Audit.Enabled || !settings.Audit.LogSettingsChanges || !settings.Audit.LogSecurityEvents) errors.Add("Demo audit controls must remain enabled.");
        if (!settings.Brand.PreserveLogoOriginalColors || !settings.Brand.ShowEnvironmentBadge) errors.Add("Demo branding must preserve the crest and show the environment badge.");
        if (!settings.Environment.SupportedCultures.Contains("ar-KW", StringComparer.OrdinalIgnoreCase) || !settings.Environment.SupportedCultures.Contains("en-US", StringComparer.OrdinalIgnoreCase))
            errors.Add("Demo must support ar-KW and en-US.");
        return errors;
    }

    private static void ValidateStorage(StorageTargetSettings target, string name, ICollection<string> errors)
    {
        Require(target.Id, name + ".Id", errors);
        Require(target.Root, name + ".Root", errors);
        if (!string.Equals(target.Type, "FileSystem", StringComparison.OrdinalIgnoreCase)) errors.Add(name + ".Type must be FileSystem in Demo.");
    }

    private static void Require(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add(name + " is required.");
    }
}
