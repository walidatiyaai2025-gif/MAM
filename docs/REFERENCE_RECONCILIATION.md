# P00 Reference Package Reconciliation

## Status

**RESOLVED for P00.** The owner supplied both the historical MAM reference package and a later buildable React/TanStack UI source package during the P00 cycle. This document records the exact package fingerprints and the architectural disposition of their reusable content.

The centralized architecture in `docs/ARCHITECTURE.md`, `docs/PRODUCT_VISION.md`, `docs/SETTINGS_REFERENCE.md`, `docs/BRANDING_UI_UX.md` and issue #1 remains authoritative whenever a historical local-only assumption conflicts with the current product target.

## Supplied package inventory

### Package A — `MAM_Reference_Code.zip`

- ZIP SHA-256: `a6bd0a9ecfe986a096756aed6aeb941ac23945ced7a036331c0d4189ceb66732`
- ZIP size: 34,912 bytes
- Package identity in its README: `MAM LOCAL - REFERENCE EXAMPLES - 1.0 - 2026-09-10`
- Contents:
  - `schema.sql` — SHA-256 `2e99f7ebbb74c458cff156aea9b362c85c7224dd49f7abf7cbb960d0821ebe5d`
  - `desktop-contract.ts` — SHA-256 `6b11f98264a19c93e627179898daa9a303164c6a19bc9bdb501f037dda21a5ab`
  - `media_reference.py` — SHA-256 `91f670234b88ca59f4ef17fa97c2bdbbb05e52070010f8d9c7c9bf9297d464cb`
  - `README.txt` — SHA-256 `741fbee7dce69dd7ada43c7342003ea1984e45b5392892e835a96d7061c5f0af`
  - `implementation_spec_ar.md` — SHA-256 `512cd0dffc5408865061051dc37c7a294b543fe64dcf1e331ee1cc9d12f50355`
  - `SHA256SUMS.txt`

The reusable small reference sources are preserved under `reference/mam-local-v1/`. The large Arabic specification is traced by exact checksum and reviewed requirement areas here; it is not treated as production code.

### Package B — `mam-desktop-source.zip`

- ZIP SHA-256: `bf8f8be51bf87c7a40d8bc898492b99a06a788c5f375b5adea2796555293277e`
- ZIP size: 5,358,140 bytes
- 148 archive entries
- Buildable UI source stack: React 19, TypeScript, TanStack Start/Router, Vite and Tailwind CSS
- Existing screens/components include Dashboard, Media Library, Asset Details, Import, Processing Queue, Collections, Storage, Settings, Activity and System views.
- Its own deployment notes explicitly classify the data layer and `DesktopBridge` as mocks and describe a future local/Tauri/SQLite direction.

Package B is therefore a **P01 visual/interaction source baseline**, not an authoritative runtime architecture. Its useful UI composition, responsive patterns, bilingual structure, asset browsing/detail views, queue UX and media controls are to be adapted into the Diwan Al Amiri product. Its local-only mock persistence, local storage ownership and Tauri-as-final-runtime assumptions are rejected.

## Compatibility / decision matrix

| Reference area | Decision | Current disposition |
|---|---|---|
| Asset / version / rendition concepts | **ACCEPT + ADAPT** | Preserve the domain separation. Implement in the central SQL Server catalog and API rather than a workstation SQLite catalog. |
| Tags, collections, comments | **ACCEPT + ADAPT** | Preserve semantics and permission checks behind Central API/Application services. |
| Processing jobs, retry state, idempotency, leases | **ACCEPT + ADAPT** | Keep durable job semantics; implement as server/worker orchestration with recovery and observable progress. |
| Stable error envelope and request IDs | **ACCEPT** | Retain stable machine-readable errors, correlation/request IDs and retryability semantics across Desktop/Web/API boundaries. |
| Stable cursor pagination | **ACCEPT** | Retain deterministic cursor-based listing semantics for large libraries. |
| SHA-256 source/rendition integrity | **ACCEPT + EXTEND** | SHA-256 remains mandatory. Extend it to the Primary/Backup protection invariant: `Protected` only after required copies independently verify. |
| Audit event chain concept | **ACCEPT + HARDEN** | Preserve centralized audit events and integrity evidence; do not rely on hashes alone against privileged DB rewrite. |
| SQLite as authoritative catalog | **REJECT for production** | Central production catalog is SQL Server. SQLite may be used only for narrowly scoped temporary client cache/state if later approved, never as the authoritative shared catalog. |
| SQLite WAL / local DB snapshot | **REJECT as production architecture** | WAL/local snapshot advice is valid only for a local SQLite implementation. Production DB backup/HA follows the SQL Server/site policy. |
| `storage_roots.absolute_path` exposed as client-owned roots | **REJECT + ADAPT** | Storage targets are server-side configured identities/endpoints. Clients receive opaque asset/upload/preview capabilities, not permanent storage credentials or arbitrary server paths. |
| Desktop `selectImportFiles` / native file picker | **ACCEPT** | Windows client may select local files, then uploads via durable central upload sessions. |
| Desktop `importFiles(copy/reference)` | **ADAPT** | `copy` becomes durable upload to central ingest; local `reference` mode is not a production permanent-media mode because clients do not own authoritative media. |
| Desktop storage-root administration | **REJECT on ordinary client** | Primary/Backup storage is privileged central administration. Capture workstation cache configuration remains workstation-specific. |
| Job subscription/reconnect recovery | **ACCEPT** | Implement push/event updates with list/recovery fallback so reconnect never fabricates state. |
| FFprobe restricted local-file inspection | **ACCEPT + HARDEN** | Keep argv-based invocation, no shell, constrained protocols, bounded output/time/resources and canonical worker-controlled paths. |
| FFmpeg Hi-Res / Low-Res profile idea | **ACCEPT + ADAPT** | Preserve profile-driven transcoding and output verification. Profiles are centrally versioned/configured; SDR/interlace/HDR/source-format prerequisites are explicit. |
| No overwrite / staging output | **ACCEPT** | Originals are immutable after ingest. Derivatives publish transactionally from staging after validation. |
| Local-only media processing | **REJECT as primary architecture** | Processing runs in central worker services. Capture-specific local work is allowed only when required by hardware and is transferred/verified centrally. |
| Local/offline product assumption | **REJECT + REDEFINE** | Product is on-premises and can operate without public cloud, but multiple Windows/Web clients connect over the institutional LAN to central services. |
| Tauri as final Windows runtime | **REJECT for current plan** | P00 ADR selects .NET 10 WPF for the hardware-facing Windows client. Package B remains a UI reference, not the native runtime. |
| React/TanStack/Tailwind UI composition | **ADAPT for Web/P01** | Reuse proven information architecture, responsive patterns and component behavior where useful, rebranded and connected to the Central API. |
| In-memory mock repository / mock DesktopBridge | **REJECT for acceptance** | Allowed only as development/reference scaffolding. No phase may claim production data, storage or capture acceptance from mocks. |
| Historical dark/blue generic design tokens | **REJECT + REBRAND** | Diwan identity is the owner-supplied crest plus authoritative Navy/Gold tokens, Arabic RTL and English LTR. |
| External web-font dependency | **REJECT for institutional/offline baseline** | Production UI must not depend on public font/CDN availability; use approved packaged/system fonts. |
| Tape capture absent from historical UI | **EXTEND** | Windows Tape Capture is a first-class product workflow: device/input health, live preview, audio meters, timecode, dropped frames, temporary ingest cache, transfer and protection status. |
| Single local backup concept | **EXTEND / REPLACE** | Use distinct Primary and Backup storage targets with copy verification, retries, health/status and central audit evidence. |

## Arabic specification requirement trace

The supplied specification contains detailed sections for purpose/limitations, implementation decisions, architecture/trust boundaries, ingest/catalog, administration, screens, storage paths, safe import, video profiles, FFmpeg verification, images/PDF, queue recovery, transactional publication, data integrity, UI/backend contracts, identity/roles, sensitive-environment controls, threat/risk model, audit/retention/deletion, backup/restore, capacity/performance, packaging/update, implementation gates and UAT.

Those requirement families are **accepted as reference input** where they do not conflict with the centralized architecture. Local-only technical choices are adapted or rejected as recorded above. The current plan additionally requires multi-client concurrency, central SQL Server/API, independent Primary/Backup storage, Web Portal, Windows tape capture and Diwan Al Amiri branding.

## P00 conclusion

The prior `DEFERRED_EXTERNAL_NOT_IN_REPOSITORY` blocker is closed. The reference material has been inventoried, fingerprinted and mapped to `ACCEPT`, `ADAPT`, `REJECT` or `EXTEND` decisions. No local-only SQLite/Tauri/storage assumption may silently re-enter the implementation.

Package B should be consumed during P01 as a source-level UI reference while production runtime behavior remains governed by the central contracts and phase gates.
