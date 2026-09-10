# Diwan Al Amiri MAM

Centralized **Media Asset Management (MAM)** platform for **Diwan Al Amiri**.

> Status: Project foundation / architecture baseline

## Product goal

Build a premium, enterprise-grade media archive that can be used from multiple Windows workstations and web browsers. End users can either:

1. **Capture media from tape / professional capture hardware** from a Windows ingest station.
2. **Upload existing media files** such as video, image, audio and PDF.

All authoritative media is stored on central server-side storage, not as a permanent local library on client workstations.

## Target platforms

- **Windows Desktop Client** — tape/device capture, file ingest, preview, metadata, search and operational workflows.
- **Web Portal** — search, browse, preview, metadata, file upload, administration, reports, audit and storage visibility.
- **Central MAM Server/API** — identity, authorization, assets, metadata, search, job orchestration, audit and policy enforcement.
- **Background Processing Workers** — proxy generation, thumbnails, media inspection, checksums, backup verification and retry jobs.

## Core storage model

```text
Windows Capture / Upload Clients      Web Portal
              \                         /
               \                       /
                +---- Central MAM API -+
                         |
                  Central Database
                         |
             +-----------+-----------+
             |                       |
      Primary Storage          Backup Storage
      authoritative copy       verified second copy
```

A workstation may use a **temporary ingest cache** to protect long tape captures from network interruption. It is never the authoritative archive. A local capture file is removed only after primary-storage verification and according to retention policy.

## Protection rule

An asset is not considered fully protected until:

- primary copy exists;
- checksum is verified;
- backup copy exists;
- backup checksum matches;
- database/catalog state is committed;
- all operations are audit logged.

## Branding and UX

The product is exclusively branded for **Diwan Al Amiri**. Desktop and web experiences must be premium, responsive, bilingual-ready (Arabic RTL / English LTR), accessible and visually consistent. Official logo and approved brand assets are configuration-controlled and must not be replaced by generic branding.

## Authoritative documentation

- [`docs/PRODUCT_VISION.md`](docs/PRODUCT_VISION.md) — product scope, users and workflows.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — system architecture and technical boundaries.
- [`docs/SETTINGS_REFERENCE.md`](docs/SETTINGS_REFERENCE.md) — complete settings and deployment configuration contract.
- [`docs/BRANDING_UI_UX.md`](docs/BRANDING_UI_UX.md) — Diwan Al Amiri branding and premium responsive UX rules.
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) — phased delivery plan and acceptance gates.
- [`PROJECT_CONTROL.md`](PROJECT_CONTROL.md) — current execution governance and source-of-truth rules.
- [`CURRENT_PHASE.md`](CURRENT_PHASE.md) — the single active delivery phase.

## Key non-negotiables

- No permanent local-only media library.
- No silent fallback from primary to backup storage for writes.
- No asset marked protected before checksum verification.
- No direct database access from desktop or web clients.
- Tape capture is a Windows capability; web is not used for professional device capture.
- File upload is supported from both Windows and web where policy permits.
- Security, auditability, resumability and recovery are first-class requirements.
- UI must be responsive and production-quality from the first user-visible phase.

## Initial technology direction

The exact implementation may evolve behind stable contracts, but the baseline is:

- Windows: .NET 10 desktop client (WPF or WinUI selected during P01 architecture spike)
- Server/API: ASP.NET Core .NET 10
- Web: modern responsive web UI consuming the same API
- Database: Microsoft SQL Server
- Media processing: FFmpeg/FFprobe plus capture-hardware adapters
- Storage: SMB/NAS/SAN/object-compatible adapter abstraction, with distinct Primary and Backup targets
- Transport: HTTPS/TLS

See the documentation before implementation. Changes that alter the storage model, capture model, security boundary or branding contract require an explicit architecture decision record.
