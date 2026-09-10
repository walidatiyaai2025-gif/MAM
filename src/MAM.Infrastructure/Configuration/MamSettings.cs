namespace MAM.Infrastructure.Configuration;

public sealed class MamSettings
{
    public EnvironmentSettings Environment { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public UploadSettings Upload { get; set; } = new();
    public BrandSettings Brand { get; set; } = new();
}

public sealed class EnvironmentSettings
{
    public string Name { get; set; } = string.Empty;
    public string SiteCode { get; set; } = string.Empty;
    public string DisplayNameAr { get; set; } = string.Empty;
    public string DisplayNameEn { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public string DefaultCulture { get; set; } = string.Empty;
    public List<string> SupportedCultures { get; set; } = [];
}

public sealed class ServerSettings
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ApiBasePath { get; set; } = string.Empty;
    public List<string> AllowedOrigins { get; set; } = [];
    public int MaxRequestBodyMB { get; set; }
    public int RequestTimeoutSeconds { get; set; }
}

public sealed class DatabaseSettings
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionStringSecretRef { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; }
    public bool EnableRetryOnFailure { get; set; }
    public string MigrationMode { get; set; } = string.Empty;
    public string BackupPolicyId { get; set; } = string.Empty;
}

public sealed class StorageSettings
{
    public PrimaryStorageTargetSettings Primary { get; set; } = new();
    public BackupStorageTargetSettings Backup { get; set; } = new();
}

public class StorageTargetSettings
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public long MinimumFreeGB { get; set; }
}

public sealed class PrimaryStorageTargetSettings : StorageTargetSettings
{
    public string OriginalsPrefix { get; set; } = string.Empty;
    public string DerivativesPrefix { get; set; } = string.Empty;
    public int MinimumFreePercent { get; set; }
    public string PathLayout { get; set; } = string.Empty;
}

public sealed class BackupStorageTargetSettings : StorageTargetSettings
{
    public bool CopyOriginals { get; set; }
    public bool CopyDerivatives { get; set; }
    public bool VerifyChecksum { get; set; }
    public int MaxRetryCount { get; set; }
}

public sealed class UploadSettings
{
    public int ChunkSizeMB { get; set; }
    public int MaxConcurrentFilesPerClient { get; set; }
    public long MaxFileSizeGB { get; set; }
    public bool ResumeEnabled { get; set; }
    public int SessionExpiryHours { get; set; }
    public string ChecksumAlgorithm { get; set; } = string.Empty;
    public string DuplicatePolicy { get; set; } = string.Empty;
    public bool WebEnabled { get; set; }
    public bool QuarantineUnknownFiles { get; set; }
}

public sealed class BrandSettings
{
    public string OrganizationNameAr { get; set; } = string.Empty;
    public string OrganizationNameEn { get; set; } = string.Empty;
    public string ProductNameAr { get; set; } = string.Empty;
    public string ProductNameEn { get; set; } = string.Empty;
    public string PrimaryLogoAsset { get; set; } = string.Empty;
    public string AppIconAsset { get; set; } = string.Empty;
    public string FaviconAsset { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public bool PreserveLogoOriginalColors { get; set; }
    public bool ShowEnvironmentBadge { get; set; }
}
