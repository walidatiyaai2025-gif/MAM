namespace MAM.Infrastructure.Configuration;

public static class MamSettingsValidator
{
    private static readonly string[] SupportedEnvironmentNames = ["Production", "UAT", "Test", "Development"];
    private static readonly string[] SupportedAuthModes = ["ActiveDirectory", "OIDC", "Local"];

    public static IReadOnlyList<string> Validate(MamSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        var errors = new List<string>();

        Require(settings.Environment.Name, "Environment.Name", errors);
        if (!string.IsNullOrWhiteSpace(settings.Environment.Name) &&
            !SupportedEnvironmentNames.Contains(settings.Environment.Name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Environment.Name must be one of Production, UAT, Test or Development.");
        }
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
        if (!string.IsNullOrWhiteSpace(settings.Environment.DefaultCulture) &&
            !settings.Environment.SupportedCultures.Contains(settings.Environment.DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Environment.DefaultCulture must be included in Environment.SupportedCultures.");
        }

        Require(settings.Server.PublicBaseUrl, "Server.PublicBaseUrl", errors);
        Require(settings.Server.ApiBasePath, "Server.ApiBasePath", errors);
        if (!string.IsNullOrWhiteSpace(settings.Server.ApiBasePath) &&
            (!settings.Server.ApiBasePath.StartsWith('/', StringComparison.Ordinal) ||
             settings.Server.ApiBasePath.Contains('?') ||
             settings.Server.ApiBasePath.Contains('#')))
        {
            errors.Add("Server.ApiBasePath must be an absolute application path beginning with '/' and must not contain query/fragment components.");
        }
        Positive(settings.Server.MaxRequestBodyMB, "Server.MaxRequestBodyMB", errors);
        Positive(settings.Server.RequestTimeoutSeconds, "Server.RequestTimeoutSeconds", errors);
        if (!settings.Server.Health.EndpointEnabled)
        {
            errors.Add("Server.Health.EndpointEnabled must be true.");
        }
        if (!Uri.TryCreate(settings.Server.PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            errors.Add("Server.PublicBaseUrl must be an absolute URL.");
        }

        var production = string.Equals(settings.Environment.Name, "Production", StringComparison.OrdinalIgnoreCase);
        if (production && publicUri is not null && publicUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Server.PublicBaseUrl must use HTTPS in Production.");
        }
        if (production && (settings.Server.AllowedOrigins.Count == 0 || settings.Server.AllowedOrigins.Any(static origin => origin == "*")))
        {
            errors.Add("Server.AllowedOrigins must be explicitly configured in Production; wildcard is forbidden.");
        }
        if (production)
        {
            foreach (var origin in settings.Server.AllowedOrigins)
            {
                if (string.IsNullOrWhiteSpace(origin) || origin.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase) ||
                    !Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme != Uri.UriSchemeHttps)
                {
                    errors.Add("Every Server.AllowedOrigins entry must be an explicit absolute HTTPS origin in Production.");
                    break;
                }
            }
        }

        if (!string.Equals(settings.Database.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Database.Provider must be SqlServer for the centralized production architecture.");
        }
        Require(settings.Database.ConnectionStringSecretRef, "Database.ConnectionStringSecretRef", errors);
        if (LooksLikePlaintextConnectionString(settings.Database.ConnectionStringSecretRef))
        {
            errors.Add("Database.ConnectionStringSecretRef must be a secret reference, not a plaintext connection string.");
        }
        Positive(settings.Database.CommandTimeoutSeconds, "Database.CommandTimeoutSeconds", errors);
        if (!settings.Database.EnableRetryOnFailure)
        {
            errors.Add("Database.EnableRetryOnFailure must be true.");
        }
        Require(settings.Database.MigrationMode, "Database.MigrationMode", errors);
        if (!string.IsNullOrWhiteSpace(settings.Database.MigrationMode) &&
            !settings.Database.MigrationMode.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.Database.MigrationMode, "Explicit", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Database.MigrationMode must be Explicit for controlled migrations.");
        }
        Require(settings.Database.BackupPolicyId, "Database.BackupPolicyId", errors);

        ValidateTarget(settings.Storage.Primary, "Storage.Primary", errors);
        Require(settings.Storage.Primary.OriginalsPrefix, "Storage.Primary.OriginalsPrefix", errors);
        ValidateManagedRelativePath(settings.Storage.Primary.OriginalsPrefix, "Storage.Primary.OriginalsPrefix", errors);
        Require(settings.Storage.Primary.DerivativesPrefix, "Storage.Primary.DerivativesPrefix", errors);
        ValidateManagedRelativePath(settings.Storage.Primary.DerivativesPrefix, "Storage.Primary.DerivativesPrefix", errors);
        Percentage(settings.Storage.Primary.MinimumFreePercent, "Storage.Primary.MinimumFreePercent", errors);
        Require(settings.Storage.Primary.PathLayout, "Storage.Primary.PathLayout", errors);
        ValidateManagedRelativePath(settings.Storage.Primary.PathLayout, "Storage.Primary.PathLayout", errors);
        if (settings.Storage.Primary.PathLayout?.Contains("{AssetId}", StringComparison.Ordinal) != true)
        {
            errors.Add("Storage.Primary.PathLayout must include {AssetId} for deterministic asset identity.");
        }

        ValidateTarget(settings.Storage.Backup, "Storage.Backup", errors);
        if (string.Equals(settings.Storage.Primary.Id, settings.Storage.Backup.Id, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage.Primary.Id and Storage.Backup.Id must be different.");
        }
        if (string.Equals(NormalizeRoot(settings.Storage.Primary.Root), NormalizeRoot(settings.Storage.Backup.Root), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage.Primary.Root and Storage.Backup.Root must resolve to distinct targets.");
        }
        if (production && (string.Equals(settings.Storage.Primary.Type, "Mock", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(settings.Storage.Backup.Type, "Mock", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Mock storage targets are forbidden in Production.");
        }
        if (!settings.Storage.Backup.CopyOriginals)
        {
            errors.Add("Storage.Backup.CopyOriginals must be true.");
        }
        if (!settings.Storage.Backup.VerifyChecksum)
        {
            errors.Add("Storage.Backup.VerifyChecksum must be true before Protected state is possible.");
        }
        Positive(settings.Storage.Backup.MaxRetryCount, "Storage.Backup.MaxRetryCount", errors);
        Positive(settings.Storage.Backup.RetryBackoffSeconds, "Storage.Backup.RetryBackoffSeconds", errors);

        Require(settings.Desktop.IngestCache.Root, "Desktop.IngestCache.Root", errors);
        Positive(settings.Desktop.IngestCache.MinimumFreeGB, "Desktop.IngestCache.MinimumFreeGB", errors);
        Percentage(settings.Desktop.IngestCache.ReservePercent, "Desktop.IngestCache.ReservePercent", errors);
        Positive(settings.Desktop.IngestCache.RetentionHoursAfterSuccess, "Desktop.IngestCache.RetentionHoursAfterSuccess", errors);
        Positive(settings.Desktop.IngestCache.RetentionDaysAfterFailure, "Desktop.IngestCache.RetentionDaysAfterFailure", errors);

        Positive(settings.Upload.ChunkSizeMB, "Upload.ChunkSizeMB", errors);
        Positive(settings.Upload.MaxConcurrentFilesPerClient, "Upload.MaxConcurrentFilesPerClient", errors);
        Positive(settings.Upload.MaxFileSizeGB, "Upload.MaxFileSizeGB", errors);
        Positive(settings.Upload.SessionExpiryHours, "Upload.SessionExpiryHours", errors);
        if (!settings.Upload.ResumeEnabled)
        {
            errors.Add("Upload.ResumeEnabled must be true.");
        }
        if (!string.Equals(settings.Upload.ChecksumAlgorithm, "SHA256", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Upload.ChecksumAlgorithm must be SHA256.");
        }
        Require(settings.Upload.DuplicatePolicy, "Upload.DuplicatePolicy", errors);
        if (!settings.Upload.WebEnabled)
        {
            errors.Add("Upload.WebEnabled must be true for the Web Portal baseline.");
        }
        Positive(settings.Upload.WebMaxConcurrentFiles, "Upload.WebMaxConcurrentFiles", errors);
        if (settings.Upload.AllowedExtensions.Count == 0 || settings.Upload.AllowedExtensions.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Upload.AllowedExtensions must contain at least one explicit non-empty extension.");
        }
        else if (settings.Upload.AllowedExtensions.Any(static extension => !IsSafeExtension(extension)))
        {
            errors.Add("Upload.AllowedExtensions entries must be extension-only values such as .mxf; paths and traversal are forbidden.");
        }

        if (settings.Capture.Enabled)
        {
            Require(settings.Capture.WorkstationId, "Capture.WorkstationId", errors);
            Require(settings.Capture.Provider, "Capture.Provider", errors);
            Require(settings.Capture.DeviceId, "Capture.DeviceId", errors);
            Require(settings.Capture.Input, "Capture.Input", errors);
            Require(settings.Capture.VideoProfile, "Capture.VideoProfile", errors);
            Require(settings.Capture.AudioProfile, "Capture.AudioProfile", errors);
            Require(settings.Capture.TimecodeSource, "Capture.TimecodeSource", errors);
            Require(settings.Capture.Container, "Capture.Container", errors);
            Require(settings.Capture.Codec, "Capture.Codec", errors);
            if (!settings.Capture.RequireLivePreview || !settings.Capture.RequireAudioMeters || !settings.Capture.RequireTapeId)
            {
                errors.Add("Enabled capture requires live preview, audio meters and Tape ID gates.");
            }
        }
        if (settings.Capture.DroppedFrameThreshold < 0)
        {
            errors.Add("Capture.DroppedFrameThreshold must not be negative.");
        }

        Positive(settings.Jobs.MaxConcurrentMediaJobsPerWorker, "Jobs.MaxConcurrentMediaJobsPerWorker", errors);
        Positive(settings.Jobs.MaxConcurrentBackupJobsPerWorker, "Jobs.MaxConcurrentBackupJobsPerWorker", errors);
        Positive(settings.Jobs.LeaseSeconds, "Jobs.LeaseSeconds", errors);
        Positive(settings.Jobs.HeartbeatSeconds, "Jobs.HeartbeatSeconds", errors);
        Positive(settings.Jobs.MaxAttempts, "Jobs.MaxAttempts", errors);
        Positive(settings.Jobs.FailedJobRetentionDays, "Jobs.FailedJobRetentionDays", errors);
        if (settings.Jobs.HeartbeatSeconds >= settings.Jobs.LeaseSeconds && settings.Jobs.LeaseSeconds > 0)
        {
            errors.Add("Jobs.HeartbeatSeconds must be less than Jobs.LeaseSeconds.");
        }
        if (!settings.Jobs.StaleJobRecoveryEnabled)
        {
            errors.Add("Jobs.StaleJobRecoveryEnabled must be true.");
        }

        Positive(settings.Search.DefaultPageSize, "Search.DefaultPageSize", errors);
        Positive(settings.Search.MaxPageSize, "Search.MaxPageSize", errors);
        if (settings.Search.DefaultPageSize > settings.Search.MaxPageSize && settings.Search.MaxPageSize > 0)
        {
            errors.Add("Search.DefaultPageSize must not exceed Search.MaxPageSize.");
        }
        if (!settings.Search.FacetsEnabled)
        {
            errors.Add("Search.FacetsEnabled must be true.");
        }

        Require(settings.Auth.Mode, "Auth.Mode", errors);
        if (!string.IsNullOrWhiteSpace(settings.Auth.Mode) &&
            !settings.Auth.Mode.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase) &&
            !SupportedAuthModes.Contains(settings.Auth.Mode, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Auth.Mode must be ActiveDirectory, OIDC or Local.");
        }
        Positive(settings.Auth.SessionIdleMinutes, "Auth.SessionIdleMinutes", errors);
        Positive(settings.Auth.AbsoluteSessionHours, "Auth.AbsoluteSessionHours", errors);
        if (string.Equals(settings.Auth.Mode, "Local", StringComparison.OrdinalIgnoreCase))
        {
            Positive(settings.Auth.MaxFailedAttempts, "Auth.MaxFailedAttempts", errors);
            Positive(settings.Auth.LockoutMinutes, "Auth.LockoutMinutes", errors);
        }

        if (!settings.Retention.SoftDeleteEnabled)
        {
            errors.Add("Retention.SoftDeleteEnabled must be true.");
        }
        Positive(settings.Retention.RecycleDays, "Retention.RecycleDays", errors);
        if (!settings.Retention.RequireReasonForDelete)
        {
            errors.Add("Retention.RequireReasonForDelete must be true.");
        }
        if (!settings.Retention.PurgePrimaryAndBackupTogether)
        {
            errors.Add("Retention.PurgePrimaryAndBackupTogether must be true to prevent inconsistent deletion.");
        }

        if (production && !settings.Audit.Enabled)
        {
            errors.Add("Audit.Enabled cannot be false in Production.");
        }
        Positive(settings.Audit.RetentionDays, "Audit.RetentionDays", errors);
        Require(settings.Audit.LogReads, "Audit.LogReads", errors);
        if (!settings.Audit.LogSettingsChanges || !settings.Audit.LogSecurityEvents)
        {
            errors.Add("Audit.LogSettingsChanges and Audit.LogSecurityEvents must be true.");
        }

        Require(settings.Logging.MinimumLevel, "Logging.MinimumLevel", errors);
        Positive(settings.Logging.FileRetentionDays, "Logging.FileRetentionDays", errors);
        if (settings.Logging.IncludeSensitiveMetadata)
        {
            errors.Add("Logging.IncludeSensitiveMetadata must be false.");
        }
        if (!settings.Logging.CorrelationIdEnabled)
        {
            errors.Add("Logging.CorrelationIdEnabled must be true.");
        }

        Require(settings.Brand.OrganizationNameAr, "Brand.OrganizationNameAr", errors);
        Require(settings.Brand.OrganizationNameEn, "Brand.OrganizationNameEn", errors);
        Require(settings.Brand.ProductNameAr, "Brand.ProductNameAr", errors);
        Require(settings.Brand.ProductNameEn, "Brand.ProductNameEn", errors);
        Require(settings.Brand.PrimaryLogoAsset, "Brand.PrimaryLogoAsset", errors);
        Require(settings.Brand.AppIconAsset, "Brand.AppIconAsset", errors);
        Require(settings.Brand.FaviconAsset, "Brand.FaviconAsset", errors);
        Require(settings.Brand.ArabicFontFamily, "Brand.ArabicFontFamily", errors);
        Require(settings.Brand.EnglishFontFamily, "Brand.EnglishFontFamily", errors);
        ValidateHexColor(settings.Brand.PrimaryColor, "Brand.PrimaryColor", errors);
        ValidateHexColor(settings.Brand.SecondaryColor, "Brand.SecondaryColor", errors);
        ValidateHexColor(settings.Brand.AccentColor, "Brand.AccentColor", errors);
        ValidateHexColor(settings.Brand.NavyDeep, "Brand.NavyDeep", errors);
        ValidateHexColor(settings.Brand.NavyHover, "Brand.NavyHover", errors);
        ValidateHexColor(settings.Brand.GoldPressed, "Brand.GoldPressed", errors);
        ValidateHexColor(settings.Brand.GoldSoft, "Brand.GoldSoft", errors);
        ValidateHexColor(settings.Brand.DangerColor, "Brand.DangerColor", errors);
        ValidateHexColor(settings.Brand.WarningColor, "Brand.WarningColor", errors);
        ValidateHexColor(settings.Brand.SuccessColor, "Brand.SuccessColor", errors);
        if (!settings.Brand.PreserveLogoOriginalColors)
        {
            errors.Add("Brand.PreserveLogoOriginalColors must be true for the owner-supplied Diwan crest.");
        }
        if (!production && !settings.Brand.ShowEnvironmentBadge)
        {
            errors.Add("Brand.ShowEnvironmentBadge must be true outside Production.");
        }
        if (production && settings.Brand.ShowEnvironmentBadge)
        {
            errors.Add("Brand.ShowEnvironmentBadge must be false in Production.");
        }

        return errors;
    }

    private static void Normalize(MamSettings settings)
    {
        settings.Environment ??= new EnvironmentSettings();
        settings.Server ??= new ServerSettings();
        settings.Server.ForwardedHeaders ??= new ForwardedHeadersSettings();
        settings.Server.Health ??= new HealthSettings();
        settings.Database ??= new DatabaseSettings();
        settings.Storage ??= new StorageSettings();
        settings.Storage.Primary ??= new PrimaryStorageTargetSettings();
        settings.Storage.Backup ??= new BackupStorageTargetSettings();
        settings.Desktop ??= new DesktopSettings();
        settings.Desktop.IngestCache ??= new IngestCacheSettings();
        settings.Upload ??= new UploadSettings();
        settings.Capture ??= new CaptureSettings();
        settings.Jobs ??= new JobsSettings();
        settings.Search ??= new SearchSettings();
        settings.Auth ??= new AuthSettings();
        settings.Retention ??= new RetentionSettings();
        settings.Audit ??= new AuditSettings();
        settings.Logging ??= new LoggingSettings();
        settings.Diagnostics ??= new DiagnosticsSettings();
        settings.Brand ??= new BrandSettings();
        settings.Environment.SupportedCultures ??= [];
        settings.Server.AllowedOrigins ??= [];
        settings.Upload.AllowedExtensions ??= [];
    }

    private static void ValidateTarget(StorageTargetSettings target, string name, ICollection<string> errors)
    {
        Require(target.Id, $"{name}.Id", errors);
        Require(target.Type, $"{name}.Type", errors);
        Require(target.Root, $"{name}.Root", errors);
        Positive(target.MinimumFreeGB, $"{name}.MinimumFreeGB", errors);
    }

    private static void Require(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{key} is required and must not contain a deployment placeholder.");
        }
    }

    private static void Positive(long value, string key, ICollection<string> errors)
    {
        if (value <= 0) errors.Add($"{key} must be greater than zero.");
    }

    private static void Percentage(int value, string key, ICollection<string> errors)
    {
        if (value <= 0 || value > 100) errors.Add($"{key} must be between 1 and 100.");
    }

    private static void ValidateManagedRelativePath(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE-WITH", StringComparison.OrdinalIgnoreCase)) return;

        if (value.StartsWith('/', StringComparison.Ordinal) || value.StartsWith('\\') || value.Contains(':') ||
            value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment == ".."))
        {
            errors.Add($"{key} must be a managed relative path with no root, drive prefix or traversal segments.");
        }
    }

    private static bool IsSafeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length < 2 || extension[0] != '.' ||
            extension.Contains('/') || extension.Contains('\\') || extension.Contains(':') || extension.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return extension[1..].All(static c => char.IsLetterOrDigit(c) || c == '.');
    }

    private static void ValidateHexColor(string? value, string key, ICollection<string> errors)
    {
        Require(value, key, errors);
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#' || !value[1..].All(static c => Uri.IsHexDigit(c)))
        {
            errors.Add($"{key} must be a #RRGGBB color.");
        }
    }

    private static bool LooksLikePlaintextConnectionString(string? value) =>
        value?.Contains("Server=", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Contains("User Id=", StringComparison.OrdinalIgnoreCase) == true;

    private static string NormalizeRoot(string? value) => (value ?? string.Empty).Trim().TrimEnd('/', '\\');
}
