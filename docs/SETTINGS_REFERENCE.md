# Settings Reference — Diwan Al Amiri MAM

This document is the authoritative configuration contract for deployment and administration. Production code must not introduce undocumented configuration keys that materially change ingest, storage, security, protection or branding behavior.

## 1. Configuration principles

- Environment-specific values are configuration, not source-code constants.
- Secrets are never committed to Git.
- Every setting has an owner, default/required state and validation rule.
- Invalid critical settings fail closed with a clear health error.
- Primary and Backup storage are configured as distinct targets.
- UI-visible branding is centrally controlled and consistent across Desktop and Web.
- Changes to security/storage/retention settings are audit logged.

## 2. Environment identity

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Environment.Name` | `Production` | Yes | Production/UAT/Test/Development |
| `Environment.SiteCode` | `DA-KW` | Yes | Short deployment/site identifier |
| `Environment.DisplayNameAr` | `نظام إدارة الأصول الإعلامية` | Yes | Arabic UI display name |
| `Environment.DisplayNameEn` | `Diwan Al Amiri Media Asset Management` | Yes | English display name |
| `Environment.TimeZone` | `Asia/Kuwait` | Yes | Used for display/business rules; timestamps remain UTC internally |
| `Environment.DefaultCulture` | `ar-KW` | Yes | Initial UI culture |
| `Environment.SupportedCultures` | `ar-KW,en-US` | Yes | Arabic RTL + English LTR baseline |

## 3. Server / API

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Server.PublicBaseUrl` | `https://mam.diwan.local` | Yes | Canonical web/API base |
| `Server.ApiBasePath` | `/api` | Yes | Stable API prefix |
| `Server.MaxRequestBodyMB` | `256` | Yes | Metadata/small uploads; large media uses upload sessions |
| `Server.AllowedOrigins` | approved web origins | Yes | No wildcard in production |
| `Server.ForwardedHeaders.Enabled` | `true` | Deployment | When behind reverse proxy/load balancer |
| `Server.Health.EndpointEnabled` | `true` | Yes | Auth policy may differ for detailed health |
| `Server.RequestTimeoutSeconds` | `120` | Yes | Must not be used as large-upload lifetime |

## 4. Database

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Database.Provider` | `SqlServer` | Yes | Production baseline |
| `Database.ConnectionString` | secret reference | Yes | Never store plaintext in repo |
| `Database.CommandTimeoutSeconds` | `60` | Yes | Tune after load testing |
| `Database.EnableRetryOnFailure` | `true` | Yes | Bounded retry |
| `Database.MigrationMode` | `Explicit` | Yes | Production migrations are controlled |
| `Database.BackupPolicyId` | `DB-DAILY` | Yes | Database backup is separate from media backup |

## 5. Primary storage

Primary storage is the authoritative media target.

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Storage.Primary.Id` | `PRIMARY-01` | Yes | Stable internal target ID |
| `Storage.Primary.Type` | `SMB` | Yes | Adapter type: SMB/NAS/SAN/Object in future |
| `Storage.Primary.Root` | `\\mam-primary\media` | Yes | Server-side path only |
| `Storage.Primary.CredentialRef` | secret reference | Deployment | Prefer service identity where possible |
| `Storage.Primary.OriginalsPrefix` | `originals` | Yes | Managed relative path |
| `Storage.Primary.DerivativesPrefix` | `derivatives` | Yes | Proxies/thumbnails |
| `Storage.Primary.MinimumFreeGB` | site-defined | Yes | New ingest blocked below safety reserve |
| `Storage.Primary.MinimumFreePercent` | site-defined | Yes | Both absolute + percentage may be enforced |
| `Storage.Primary.WriteTestOnHealthCheck` | `true` | Recommended | Uses safe temp probe |
| `Storage.Primary.PathLayout` | `yyyy/MM/{AssetId}` | Yes | Must be deterministic and centrally generated |

## 6. Backup storage

Backup storage must be a distinct protection target.

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Storage.Backup.Id` | `BACKUP-01` | Yes | Must differ from Primary ID |
| `Storage.Backup.Type` | `SMB` | Yes | Adapter type |
| `Storage.Backup.Root` | `\\mam-backup\media` | Yes | Separate target |
| `Storage.Backup.CredentialRef` | secret reference | Deployment | Least privilege |
| `Storage.Backup.MinimumFreeGB` | site-defined | Yes | Alert/block rules configurable |
| `Storage.Backup.CopyOriginals` | `true` | Yes | Required baseline |
| `Storage.Backup.CopyDerivatives` | policy | Yes | Optional based on recovery objective |
| `Storage.Backup.VerifyChecksum` | `true` | Yes | Mandatory for Protected status |
| `Storage.Backup.MaxRetryCount` | `10` | Yes | Durable retries after this remain visible/failed |
| `Storage.Backup.RetryBackoffSeconds` | policy | Yes | Exponential/backoff profile |
| `Storage.Backup.IntegrityRecheckSchedule` | site-defined | Recommended | Periodic sample/full verification policy |

### Protection invariant

`Asset.ProtectionState = Protected` only when the Primary original and Backup original are independently readable and checksum-verified with matching SHA-256.

## 7. Windows ingest cache

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Desktop.IngestCache.Root` | `D:\DiwanMAM\IngestCache` | Capture clients | Temporary only |
| `Desktop.IngestCache.MinimumFreeGB` | site-defined | Yes | Preflight capture protection |
| `Desktop.IngestCache.ReservePercent` | `10` | Recommended | Prevent full disk |
| `Desktop.IngestCache.DeleteAfterPrimaryVerified` | policy | Yes | Backup completion may also be required |
| `Desktop.IngestCache.RetentionHoursAfterSuccess` | `24` | Recommended | Recovery buffer |
| `Desktop.IngestCache.RetentionDaysAfterFailure` | `7` | Recommended | Admin/operator cleanup after investigation |
| `Desktop.IngestCache.EncryptionRequired` | deployment policy | Yes | Depends on workstation/security standard |

## 8. Upload and transfer

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Upload.ChunkSizeMB` | `16` | Yes | Tune for LAN/storage behavior |
| `Upload.MaxConcurrentFilesPerClient` | `3` | Yes | Prevent one workstation monopolizing resources |
| `Upload.MaxFileSizeGB` | site-defined | Yes | Must support large broadcast masters |
| `Upload.ResumeEnabled` | `true` | Yes | Mandatory |
| `Upload.SessionExpiryHours` | `72` | Yes | Incomplete session cleanup |
| `Upload.ChecksumAlgorithm` | `SHA256` | Yes | Baseline integrity algorithm |
| `Upload.DuplicatePolicy` | `DetectAndPrompt` | Yes | Hash duplicate handling is explicit |
| `Upload.WebEnabled` | `true` | Yes | File ingest only, no tape capture |
| `Upload.WebMaxConcurrentFiles` | `3` | Yes | Browser limit |
| `Upload.AllowedExtensions` | policy list | Yes | Server validates content too |
| `Upload.QuarantineUnknownFiles` | `true` | Recommended | Do not silently accept unsupported content |

## 9. Capture stations and hardware

Each approved workstation has a server-managed profile.

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Capture.Enabled` | `true` | Capture station | False for ordinary desktop clients |
| `Capture.WorkstationId` | `INGEST-01` | Yes | Stable registered identity |
| `Capture.Provider` | `Blackmagic` / `AJA` / `Mock` | Yes | Adapter/provider |
| `Capture.DeviceId` | vendor identifier | Yes | Selected certified device |
| `Capture.Input` | `SDI` | Yes | Device-profile constrained |
| `Capture.VideoProfile` | `1080i50` | Yes | Site profile |
| `Capture.AudioProfile` | `PCM-48K-2CH` | Yes | Site profile |
| `Capture.TimecodeSource` | `Embedded` | Yes | Embedded/LTC/VITC/System/Manual as supported |
| `Capture.Container` | site-defined | Yes | Preservation/ingest format |
| `Capture.Codec` | site-defined | Yes | Determined after hardware/source validation |
| `Capture.DroppedFrameThreshold` | `0` | Recommended | Any drop can trigger warning/failure policy |
| `Capture.RequireLivePreview` | `true` | Yes | Operator confidence |
| `Capture.RequireAudioMeters` | `true` | Yes | Operator confidence |
| `Capture.RequireTapeId` | `true` | Yes | Metadata gate |
| `Capture.AutoUploadAfterStop` | `true` | Yes | Default operational flow |

### Capture device certification

A device/profile is not Production-approved until the project records:
- exact hardware model;
- driver/SDK version;
- supported input formats;
- timecode behavior;
- audio mapping;
- sustained capture test duration;
- dropped-frame result;
- recovery behavior after application/network interruption.

## 10. Media processing profiles

Processing profiles are centrally managed and versioned.

Suggested baseline profile fields:

- profile ID/version;
- applicable media family/source format;
- proxy container/codec;
- proxy width/height/frame-rate policy;
- video bitrate/quality mode;
- audio codec/bitrate/channel mapping;
- thumbnail count/time offset;
- contact-sheet settings;
- hardware acceleration policy;
- maximum concurrent jobs;
- timeout/watchdog policy.

Do not hard-code one resolution for every source. Maintain aspect ratio and avoid accidental upscaling unless a profile explicitly permits it.

## 11. Job system

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Jobs.MaxConcurrentMediaJobsPerWorker` | hardware-based | Yes | Capacity controlled |
| `Jobs.MaxConcurrentBackupJobsPerWorker` | storage-based | Yes | Avoid saturating backup |
| `Jobs.LeaseSeconds` | `120` | Yes | Durable worker lease |
| `Jobs.HeartbeatSeconds` | `30` | Yes | Detect abandoned jobs |
| `Jobs.MaxAttempts` | job-type policy | Yes | Never infinite silent retry |
| `Jobs.StaleJobRecoveryEnabled` | `true` | Yes | Restart recovery |
| `Jobs.FailedJobRetentionDays` | site-defined | Yes | Evidence/diagnostics |

## 12. Metadata policy

Administrators can configure:
- required fields by asset/ingest type;
- Arabic/English title requirements;
- collection/category dictionaries;
- controlled tag vocabularies;
- tape-type dictionary;
- source/event date rules;
- classification/rights fields;
- retention classes;
- custom fields through a constrained schema rather than arbitrary database columns.

Every schema/template change is versioned and audit logged.

## 13. Search

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Search.DefaultPageSize` | `50` | Yes | Responsive paging |
| `Search.MaxPageSize` | `200` | Yes | Protect server |
| `Search.HighlightMatches` | `true` | Recommended | UX |
| `Search.FacetsEnabled` | `true` | Yes | Media type, date, collection, tags, protection state etc. |
| `Search.IncludeArchivedByDefault` | `false` | Yes | Explicit archived filter |

## 14. Authentication

Supported mode is deployment-controlled.

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Auth.Mode` | `ActiveDirectory` / `OIDC` / `Local` | Yes | Final production selection required |
| `Auth.SessionIdleMinutes` | `30` | Yes | Security policy |
| `Auth.AbsoluteSessionHours` | `12` | Yes | Security policy |
| `Auth.MaxFailedAttempts` | policy | Yes | Local-auth mode |
| `Auth.LockoutMinutes` | policy | Yes | Local-auth mode |
| `Auth.RequireMfa` | IdP policy | Deployment | Enforced through identity provider when available |
| `Auth.AllowRememberMe` | `false` | Recommended | Shared/controlled workstations |

## 15. Authorization / RBAC

Permissions are granular and server-side. Minimum permission groups:

- `asset.view`
- `asset.preview`
- `asset.download_original`
- `asset.download_proxy`
- `asset.create_upload`
- `asset.capture`
- `asset.edit_metadata`
- `asset.archive`
- `asset.restore`
- `asset.delete`
- `collection.manage`
- `queue.view`
- `queue.retry`
- `user.manage`
- `role.manage`
- `settings.view`
- `settings.manage`
- `storage.manage`
- `audit.view`
- `report.view`

## 16. Retention and deletion

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Retention.SoftDeleteEnabled` | `true` | Yes | Normal deletion is reversible first |
| `Retention.RecycleDays` | site-defined | Yes | Before eligible purge |
| `Retention.RequireApprovalForPermanentDelete` | `true` | Recommended | Enterprise safeguard |
| `Retention.LegalHoldEnabled` | `true` | Recommended | Prevent purge when held |
| `Retention.RequireReasonForDelete` | `true` | Yes | Audit evidence |
| `Retention.PurgePrimaryAndBackupTogether` | controlled workflow | Yes | Avoid inconsistent silent deletion |

## 17. Export / download

Settings include:
- original-download permission;
- proxy-download permission;
- export package manifest;
- checksum sidecar generation;
- watermark policy for preview/export when later required;
- export destination restrictions;
- maximum concurrent exports;
- audit policy for downloads/exports.

## 18. Audit

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Audit.Enabled` | `true` | Yes | Cannot be disabled in production |
| `Audit.RetentionDays` | policy | Yes | Institutional requirement |
| `Audit.LogReads` | selective | Yes | Balance privacy/volume |
| `Audit.LogDownloads` | `true` | Recommended | Traceability |
| `Audit.LogSettingsChanges` | `true` | Yes | Mandatory |
| `Audit.LogSecurityEvents` | `true` | Yes | Mandatory |
| `Audit.ExportEnabled` | permission controlled | Yes | Auditor workflow |

## 19. System logs and diagnostics

| Setting | Example | Required | Notes |
|---|---|---:|---|
| `Logging.MinimumLevel` | `Information` | Yes | Production baseline |
| `Logging.FileRetentionDays` | `30` | Yes | Operational logs; audit separate |
| `Logging.IncludeSensitiveMetadata` | `false` | Yes | Fail safe |
| `Logging.CorrelationIdEnabled` | `true` | Yes | Cross-component diagnostics |
| `Diagnostics.ClientBundleEnabled` | `true` | Recommended | Sanitized support package |

## 20. Notifications and alerts

Support configurable channels later without coupling business logic. Alert conditions baseline:
- Primary storage unreachable;
- Backup storage unreachable;
- low capacity;
- backup verification failed;
- repeated processing failure;
- queue backlog threshold;
- capture device unavailable;
- DB backup stale/failing;
- worker offline.

Initial delivery may display alerts in system dashboard even before email/SMS integration.

## 21. Database and configuration backup

Media backup does not replace application-state backup.

Configure:
- SQL full/differential/log backup strategy;
- configuration backup/export;
- encryption-key backup/recovery where applicable;
- restore test schedule;
- target RPO/RTO;
- recovery runbook owner.

## 22. Branding settings

| Setting | Required | Notes |
|---|---:|---|
| `Brand.OrganizationNameAr` | Yes | `الديوان الأميري` using the officially approved spelling supplied/confirmed by owner |
| `Brand.OrganizationNameEn` | Yes | Approved English organization name |
| `Brand.ProductNameAr` | Yes | Approved product title |
| `Brand.ProductNameEn` | Yes | Approved product title |
| `Brand.PrimaryLogoAsset` | Yes | Official provided asset only |
| `Brand.CompactLogoAsset` | Recommended | For small/navigation contexts |
| `Brand.AppIconAsset` | Yes | Approved application icon |
| `Brand.FaviconAsset` | Web | Approved web icon |
| `Brand.LoginBackgroundAsset` | Optional | Must remain institutional/professional |
| `Brand.PrimaryColor` | Yes | Set from approved brand palette, not guessed |
| `Brand.SecondaryColor` | Yes | Approved palette |
| `Brand.AccentColor` | Yes | Approved palette |
| `Brand.Danger/Warning/Success` | Yes | Accessible semantic colors |
| `Brand.ArabicFontFamily` | Yes | Licensed/approved and legible |
| `Brand.EnglishFontFamily` | Yes | Licensed/approved and legible |
| `Brand.FooterTextAr/En` | Yes | Centrally managed |
| `Brand.ShowEnvironmentBadge` | Yes | Mandatory outside Production |

Official logo files, exact brand colors and fonts remain **owner-provided/approved inputs**. Development must use replaceable placeholders until those exact assets are supplied; no developer may invent a lookalike government seal/logo.

## 23. UI preferences

User-level preferences may include:
- Arabic/English;
- light/dark theme if approved;
- grid/list library view;
- result density;
- remembered filter presets;
- default landing page;
- preview volume/mute;
- table column preferences.

Security-critical settings are never user-overridable.

## 24. Web responsiveness breakpoints

Implementation must be fluid; breakpoints are guidance, not fixed device targets:
- compact/mobile: approximately `< 640px`;
- tablet: `640–1023px`;
- desktop: `1024–1439px`;
- wide operations screen: `>= 1440px`.

Critical workflows must remain usable at 360px CSS width, but professional tape capture is Desktop-only and optimized for workstation displays.

## 25. Desktop display support

Windows UI must handle:
- 1366x768 minimum operational layout;
- 1920x1080 primary target;
- 2560x1440/4K scaling;
- Windows display scaling 100–200%;
- keyboard operation and high-DPI rendering;
- RTL/LTR without clipped controls.

## 26. Production readiness settings checklist

Before Production, the deployment owner must explicitly provide/approve:

1. Production FQDN/TLS certificate strategy.
2. SQL Server instance/HA/backup policy.
3. Primary Storage endpoint, capacity and service identity.
4. Backup Storage endpoint, capacity and independence model.
5. Expected concurrent users and ingest stations.
6. Network bandwidth/VLAN/firewall/DNS/NTP details.
7. Capture card/deck models, inputs and drivers/SDKs.
8. Source tape/video formats and desired preservation codec/container.
9. Authentication mode and directory/IdP integration.
10. User/role approval model.
11. Metadata dictionaries and mandatory fields.
12. Retention/deletion/legal-hold rules.
13. Official Diwan Al Amiri logo/icon/brand palette/fonts.
14. Audit/log retention requirement.
15. Database RPO/RTO and disaster-recovery target.
16. Proxy quality/profile requirements.
17. Antivirus/malware scanning integration requirement.
18. Export/download policy.
19. Monitoring/alert recipients and channels.
20. UAT workstation/browser matrix.

Missing production values must be tracked as explicit deployment dependencies and must never be silently guessed.
