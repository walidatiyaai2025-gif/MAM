# Architecture — Diwan Al Amiri MAM

## 1. Architecture objective

The platform is a centralized client/server MAM. Multiple Windows clients and web users share one authoritative catalog and server-side storage model.

```text
+----------------------+       +----------------------+
| Windows Desktop      |       | Responsive Web       |
| Capture + Upload     |       | Search + Upload      |
| Preview + Metadata   |       | Admin + Reports      |
+----------+-----------+       +----------+-----------+
           \                              /
            \          HTTPS/TLS          /
             +-----------+---------------+
                         |
                 +-------v--------+
                 | Central MAM API|
                 | Auth / Assets  |
                 | Search / Audit |
                 +---+---------+--+
                     |         |
           +---------+         +-------------------+
           |                                     |
   +-------v--------+                    +-------v--------+
   | SQL Server     |                    | Job Orchestrator|
   | Catalog/State  |                    | Processing      |
   +----------------+                    +---+----------+---+
                                            |          |
                                   +--------v--+   +---v---------+
                                   | Primary   |   | Backup       |
                                   | Storage   |   | Storage      |
                                   +-----------+   +--------------+
```

## 2. Logical components

### MAM.Desktop
Windows desktop application used by ingest operators and power users.

Responsibilities:
- authentication/session handling;
- capture-device integration;
- live video preview/audio meters/timecode;
- temporary ingest cache;
- resumable upload;
- local transfer recovery after crash/reboot;
- file ingest;
- search/preview/metadata workflows;
- diagnostics for workstation/device/network readiness.

### MAM.Web
Responsive browser application using the same API.

Responsibilities:
- dashboard;
- media library/search;
- asset detail/preview;
- metadata and collections;
- file upload with chunking/resume where browser capability permits;
- queue/status visibility;
- administration, reports and audit views.

### MAM.Api
Authoritative application boundary.

Responsibilities:
- authentication/authorization;
- asset identity and lifecycle;
- metadata validation;
- storage policy;
- upload session orchestration;
- search API;
- audit events;
- settings/configuration access;
- health/status endpoints.

Clients must never receive database credentials.

### MAM.Worker
Background workers for durable asynchronous processing.

Responsibilities:
- FFprobe inspection;
- checksum calculation/verification;
- proxy transcoding;
- thumbnail/contact-sheet generation;
- backup copy and verification;
- retry/backoff;
- orphan recovery;
- periodic integrity verification;
- housekeeping governed by retention policy.

### SQL Server
Stores catalog and system state; media binaries are not stored inside the relational database.

### Primary Storage
Authoritative media storage target. Supports originals and configurable derivatives/proxies. The API/worker service identity owns server-side writes.

### Backup Storage
Independent second copy. It has a separate configuration and health state. Normal reads do not silently fail over to it; restore/failover is an explicit controlled workflow.

## 3. Storage abstraction

Implement storage through an adapter contract so infrastructure can use SMB/NAS/SAN initially while allowing future object storage or archive tiers.

Each asset copy records:
- storage target ID;
- relative object/path key;
- length;
- checksum algorithm and value;
- write completed timestamp;
- verification timestamp;
- health/protection state.

Never store only a raw UNC path as the asset identity.

## 4. Temporary ingest cache

Tape capture writes first to a local cache to protect against network interruptions.

Requirements:
- configurable root path and capacity reserve;
- preflight disk-space check;
- per-job working directory;
- journal/state file sufficient for recovery;
- no automatic deletion until primary verification succeeds and cache-retention policy allows deletion;
- operator-visible status and recovery action;
- encryption at rest if required by deployment policy.

## 5. Transfer protocol

For file/tape ingest, use durable upload sessions:

- create upload session;
- transfer chunks/segments;
- persist server-side received ranges;
- resume after interruption;
- finalize;
- verify size + SHA-256;
- commit storage object;
- atomically promote asset lifecycle state.

Large media must not depend on one long HTTP request.

## 6. Media processing

Baseline processing uses FFmpeg/FFprobe behind a versioned `IMediaProcessor` abstraction.

Processing profiles are configuration data, not hard-coded constants. Profiles may define:
- preservation/original handling;
- preview/proxy video codec, frame size and bitrate;
- audio codec/bitrate/channel handling;
- thumbnail extraction;
- contact sheet;
- waveform/technical metadata where later required.

Original media is never transcoded in place.

## 7. Tape capture adapter

Capture hardware is isolated behind an adapter boundary, for example:

`ICaptureDeviceProvider -> ICaptureDeviceSession -> CaptureResult`

This permits certified adapters for Blackmagic/AJA/other approved devices without coupling the whole desktop application to one vendor SDK.

Each device profile records supported input connectors, frame formats, audio mapping, timecode source, codec/container target and validation rules.

## 8. Identity and authorization

Support enterprise identity as a deployment decision. Architecture must allow:
- local application accounts for isolated/test environments;
- Windows/Active Directory or OIDC integration for production when approved;
- role + permission policy;
- MFA if identity provider supplies it;
- session timeout and lockout settings;
- separation of administrative and operational permissions.

Authorization is enforced server-side for every sensitive operation.

## 9. Search

Phase 1 may use SQL Server indexed metadata/full-text features where adequate. Search contract must remain independent enough to add a dedicated search engine later without changing client workflows.

## 10. Audit

Audit is append-oriented and server-authoritative. Record security and business events such as:
- login/logout/failure;
- capture start/stop/failure;
- upload start/finalize/failure;
- metadata changes;
- downloads/exports where policy requires;
- delete/archive/restore;
- permission/settings changes;
- storage/backup verification outcomes;
- administrative actions.

Audit records must include actor, timestamp, action, target, workstation/client context, result and correlation ID while avoiding secret leakage.

## 11. Resilience

Mandatory design properties:
- idempotent background jobs;
- durable queues/state;
- restart-safe upload/capture recovery;
- retries with bounded exponential backoff;
- no duplicate assets caused by retry races;
- concurrency control on mutable asset metadata;
- database migrations with rollback/recovery procedure;
- health probes for DB, primary storage, backup storage and workers;
- graceful degraded states with explicit operator messaging.

## 12. Security baseline

- HTTPS/TLS only for production API/web.
- Secrets are externalized from source control.
- Service accounts use least privilege.
- Client-supplied paths are never trusted as server paths.
- Validate MIME/container/signature and extension policy.
- Malware scanning hook for uploaded documents/files where enterprise tooling is available.
- Strict filename/path normalization.
- Content Security Policy and secure cookie/token practices for web.
- No sensitive metadata in client logs by default.
- Central log retention and audit policy are configurable.

## 13. Network baseline

Deployment must document:
- API endpoint/FQDN;
- DNS/NTP requirements;
- desktop-to-API ports;
- server/worker-to-storage ports;
- database connectivity restricted to server-side components;
- recommended 1/10 GbE for ingest stations depending on source bitrate and concurrency;
- bandwidth/concurrency limits;
- proxy streaming strategy.

## 14. Observability

Provide:
- structured logs with correlation IDs;
- system dashboard;
- worker queue depth;
- failed jobs;
- upload throughput;
- capture dropped frames/errors;
- primary/backup capacity and accessibility;
- last successful backup verification;
- database/worker health;
- version/build information.

## 15. Deployment units

Initial deployables:

1. `DiwanMAM-Desktop-Setup-x64.exe`
2. `DiwanMAM.Server` deployment package
3. `DiwanMAM.Worker` deployment package/service
4. `DiwanMAM.Web` deployment package
5. SQL migration bundle
6. configuration template + deployment validation utility

The desktop should be self-contained where practical so client machines do not require manual .NET runtime installation.
