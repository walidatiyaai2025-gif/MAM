namespace MAM.Infrastructure.Configuration;

public sealed class MamSettings
{
    public EnvironmentSettings Environment { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public DesktopSettings Desktop { get; set; } = new();
    public UploadSettings Upload { get; set; } = new();
    public CaptureSettings Capture { get; set; } = new();
    public JobsSettings Jobs { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public AuthSettings Auth { get; set; } = new();
    public RetentionSettings Retention { get; set; } = new();
    public AuditSettings Audit { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
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
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();
    public HealthSettings Health { get; set; } = new();
}

public sealed class ForwardedHeadersSettings
{
    public bool Enabled { get; set; }
}

public sealed class HealthSettings
{
    public bool EndpointEnabled { get; set; }
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
    public bool WriteTestOnHealthCheck { get; set; }
    public string PathLayout { get; set; } = string.Empty;
}

public sealed class BackupStorageTargetSettings : StorageTargetSettings
{
    public bool CopyOriginals { get; set; }
    public bool CopyDerivatives { get; set; }
    public bool VerifyChecksum { get; set; }
    public int MaxRetryCount { get; set; }
    public int RetryBackoffSeconds { get; set; }
    public string IntegrityRecheckSchedule { get; set; } = string.Empty;
}

public sealed class DesktopSettings
{
    public IngestCacheSettings IngestCache { get; set; } = new();
}

public sealed class IngestCacheSettings
{
    public string Root { get; set; } = string.Empty;
    public long MinimumFreeGB { get; set; }
    public int ReservePercent { get; set; }
    public bool DeleteAfterPrimaryVerified { get; set; }
    public int RetentionHoursAfterSuccess { get; set; }
    public int RetentionDaysAfterFailure { get; set; }
    public bool EncryptionRequired { get; set; }
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
    public int WebMaxConcurrentFiles { get; set; }
    public List<string> AllowedExtensions { get; set; } = [];
    public bool QuarantineUnknownFiles { get; set; }
}

public sealed class CaptureSettings
{
    public bool Enabled { get; set; }
    public string WorkstationId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string VideoProfile { get; set; } = string.Empty;
    public string AudioProfile { get; set; } = string.Empty;
    public string TimecodeSource { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int DroppedFrameThreshold { get; set; }
    public bool RequireLivePreview { get; set; }
    public bool RequireAudioMeters { get; set; }
    public bool RequireTapeId { get; set; }
    public bool AutoUploadAfterStop { get; set; }
}

public sealed class JobsSettings
{
    public int MaxConcurrentMediaJobsPerWorker { get; set; }
    public int MaxConcurrentBackupJobsPerWorker { get; set; }
    public int LeaseSeconds { get; set; }
    public int HeartbeatSeconds { get; set; }
    public int MaxAttempts { get; set; }
    public bool StaleJobRecoveryEnabled { get; set; }
    public int FailedJobRetentionDays { get; set; }
}

public sealed class SearchSettings
{
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
    public bool HighlightMatches { get; set; }
    public bool FacetsEnabled { get; set; }
    public bool IncludeArchivedByDefault { get; set; }
}

public sealed class AuthSettings
{
    public string Mode { get; set; } = string.Empty;
    public int SessionIdleMinutes { get; set; }
    public int AbsoluteSessionHours { get; set; }
    public int MaxFailedAttempts { get; set; }
    public int LockoutMinutes { get; set; }
    public bool RequireMfa { get; set; }
    public bool AllowRememberMe { get; set; }
}

public sealed class RetentionSettings
{
    public bool SoftDeleteEnabled { get; set; }
    public int RecycleDays { get; set; }
    public bool RequireApprovalForPermanentDelete { get; set; }
    public bool LegalHoldEnabled { get; set; }
    public bool RequireReasonForDelete { get; set; }
    public bool PurgePrimaryAndBackupTogether { get; set; }
}

public sealed class AuditSettings
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; }
    public string LogReads { get; set; } = string.Empty;
    public bool LogDownloads { get; set; }
    public bool LogSettingsChanges { get; set; }
    public bool LogSecurityEvents { get; set; }
    public bool ExportEnabled { get; set; }
}

public sealed class LoggingSettings
{
    public string MinimumLevel { get; set; } = string.Empty;
    public int FileRetentionDays { get; set; }
    public bool IncludeSensitiveMetadata { get; set; }
    public bool CorrelationIdEnabled { get; set; }
}

public sealed class DiagnosticsSettings
{
    public bool ClientBundleEnabled { get; set; }
}

public sealed class BrandSettings
{
    public string OrganizationNameAr { get; set; } = string.Empty;
    public string OrganizationNameEn { get; set; } = string.Empty;
    public string ProductNameAr { get; set; } = string.Empty;
    public string ProductNameEn { get; set; } = string.Empty;
    public string PrimaryLogoAsset { get; set; } = string.Empty;
    public string CompactLogoAsset { get; set; } = string.Empty;
    public string AppIconAsset { get; set; } = string.Empty;
    public string FaviconAsset { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public string NavyDeep { get; set; } = string.Empty;
    public string NavyHover { get; set; } = string.Empty;
    public string GoldPressed { get; set; } = string.Empty;
    public string GoldSoft { get; set; } = string.Empty;
    public string DangerColor { get; set; } = string.Empty;
    public string WarningColor { get; set; } = string.Empty;
    public string SuccessColor { get; set; } = string.Empty;
    public string ArabicFontFamily { get; set; } = string.Empty;
    public string EnglishFontFamily { get; set; } = string.Empty;
    public bool PreserveLogoOriginalColors { get; set; }
    public bool ShowEnvironmentBadge { get; set; }
}
