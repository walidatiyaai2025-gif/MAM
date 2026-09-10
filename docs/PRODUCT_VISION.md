# Product Vision — Diwan Al Amiri MAM

## 1. Mission

Provide Diwan Al Amiri with a centralized, secure and resilient Media Asset Management platform for ingesting, preserving, searching, viewing and governing institutional media across multiple workstations and browsers.

The system is an archive and operational media-management platform, not a video editor.

## 2. Primary user journeys

### 2.1 Tape capture / digitization

1. User signs in on an authorized Windows ingest station.
2. User selects **Capture from Tape**.
3. Client detects/validates the configured capture device and input profile.
4. User enters or scans Tape ID and required metadata.
5. Live preview, audio meters, source format and timecode are shown.
6. Recording writes to a protected temporary ingest cache.
7. On stop/completion, the client finalizes the file and computes SHA-256.
8. File is transferred/resumed to Primary Storage.
9. Server verifies the primary copy.
10. Asset is catalogued and processing jobs start.
11. Backup worker copies to Backup Storage and verifies checksum parity.
12. Local temporary media is deleted only when policy permits.

### 2.2 Existing-file ingest

Supported from Windows and, where configured, Web Portal.

1. User selects one or more files.
2. Client performs preflight checks.
3. Metadata is entered or inherited from ingest templates.
4. Upload is chunked/resumable for large files.
5. Server performs type validation and checksum verification.
6. Original is committed to Primary Storage.
7. Proxies/thumbnails/technical metadata are generated.
8. Verified second copy is created on Backup Storage.

### 2.3 Search / discovery / preview

Users can search by free text and structured filters such as title, date, collection, media type, tags, tape ID, ingest source, operator, checksum, technical format and protection state. Search results open a premium asset detail view with preview, metadata, versions/derivatives, activity, comments/notes, storage protection state and audit-visible history according to permissions.

### 2.4 Administration

Authorized administrators manage users/roles, storage targets, ingest templates, device profiles, metadata dictionaries, retention, processing profiles, backup policy, security, branding assets, localization and system health.

## 3. Supported asset families

Baseline:

- Video
- Image
- Audio
- PDF / document attachment where policy permits

The model must be extensible without changing asset identity semantics.

## 4. Tape and capture metadata

At minimum the data model must support:

- Asset ID
- Tape ID / barcode
- Tape type / source family
- Title / Arabic title / English title
- Description / notes
- Collection / category / tags
- Source date / event date
- Capture date
- Operator
- Capture workstation
- Capture device
- Input connector/profile
- Source video/audio format
- Timecode in/out
- Duration
- Dropped-frame/error counters
- Original filename
- File size
- SHA-256
- Primary storage location/state
- Backup storage location/state
- Proxy/thumbnail state
- Rights/classification/retention fields when enabled

## 5. Roles baseline

The authorization model is policy-driven and least-privilege. Initial role templates:

- **System Administrator** — platform configuration and diagnostics.
- **Media Administrator** — asset governance, metadata dictionaries and operational oversight.
- **Ingest Operator** — tape capture and file ingest.
- **Archivist / Editor** — metadata curation, organization and approved asset operations.
- **Viewer** — search, preview and allowed downloads.
- **Auditor** — read-only access to audit/protection evidence.

Roles are templates; permissions are authoritative.

## 6. Asset lifecycle

Suggested states:

`Draft -> Capturing/Uploading -> Received -> Verifying -> Processing -> Available -> Protected`

Exceptional states include `Failed`, `Quarantined`, `BackupPending`, `Archived`, `RestorePending` and `Deleted/RetentionHold` according to policy.

**Protected** means both authoritative copies are checksum-verified. UI must not use a green/safe state before this condition is true.

## 7. Core capabilities

- Multi-user centralized catalog
- Tape/device capture on Windows
- Multi-file upload on Windows and web
- Resumable transfers
- Temporary capture cache and crash recovery
- Original preservation
- Technical media inspection
- Proxy and thumbnail generation
- Search and faceted filtering
- Collections/tags/categories
- Metadata templates and required-field policy
- Asset version/derivative tracking
- Comments/notes where permitted
- Primary + backup storage verification
- Processing/retry queue
- Users, roles and permissions
- Full audit trail
- Backup/restore of database/configuration
- Reports and operational dashboards
- System health and storage capacity monitoring
- Arabic RTL and English LTR
- Responsive premium UX

## 8. Explicit architectural boundaries

- Windows capture stations may access capture hardware and temporary local cache.
- Desktop and web clients communicate with the Central API; they do not query SQL Server directly.
- Permanent originals belong to server-managed storage.
- Backup Storage is a separate protection target, not the normal write target.
- Web browsers do not perform professional tape/device capture.
- Backup is verified data protection, not merely a scheduled file copy.

## 9. Future-ready features, not required for initial production

These may be added behind explicit phases and policies:

- OCR
- Speech-to-text
- AI semantic search
- Face/object recognition
- Automated scene detection
- Broadcast integrations
- LTO library integration
- External delivery portals
- Watermarking and approval workflows

They must not block delivery of the reliable ingest/archive core.

## 10. Product success criteria

The application is successful when an authorized user can capture or upload media from any approved ingest station, see the same catalog from another workstation or browser, find and preview that asset, and receive clear proof that the original exists on Primary Storage and an independently verified copy exists on Backup Storage — without relying on the capture workstation as permanent storage.
