# Task Ledger — Diwan Al Amiri MAM

## P00 — Foundation & Reference Reconciliation — CLOSED

| Unit | Status | Closure evidence |
|---|---|---|
| P00::repository-structure | CLOSED | `MAM.sln`; Desktop/Web/API/Worker/Domain/Application/Infrastructure/checks integrated on `main`; client dependency direction guarded. |
| P00::engineering-contribution-standards | CLOSED | `CONTRIBUTING.md` + `AGENTS.md` integrated. |
| P00::ci-clean-build | CLOSED | PR CI run #70 / `34510822705` SUCCESS; exact-main CI run #71 / `34511050672` SUCCESS. |
| P00::build-metadata | CLOSED | Shared build identity surfaced by deployables and checked in CI. |
| P00::configuration-binding-validation | CLOSED | Typed settings binding, strict unknown-key rejection and fail-closed semantic validation covered by foundation acceptance checks. |
| P00::development-config-template | CLOSED | Secret-free development template integrated; production template intentionally requires site values. |
| P00::central-architecture-adr | CLOSED | ADR 0001 fixes Central API, SQL Server catalog, server-side Primary/Backup and temporary workstation cache boundaries. |
| P00::desktop-technology-adr | CLOSED | ADR 0002 selects WPF/.NET 10. |
| P00::upload-storage-contract-adr | CLOSED | ADR 0003 and upload/storage application contracts integrated. |
| P00::capture-boundary-adr | CLOSED | ADR 0004 + `ICaptureProvider` establish Windows capture boundary and temporary-cache → Central API → Primary → verified Backup handoff. |
| P00::security-dependency-baseline | CLOSED | Repository secret baseline and dependency vulnerability baseline succeeded in PR and exact-main CI. |
| P00::reference-package-reconciliation | CLOSED | Reference package inventoried/reconciled; safe reusable sources preserved under `reference/mam-local-v1/`; UI source package registered as P01 visual baseline only. |
| P00::development-runbook | CLOSED | README/run instructions integrated and synchronized with architecture/configuration boundaries. |

### P00 integration evidence

- Final blocker fix: commit `5f5bb874eccb0eae5f89d8864906ae7c90ac0733`.
- PR #2 merged successfully.
- Integrated main SHA used for phase-exit verification: `d43b4e56000e8d6b768d8e5a32423ec10368156f`.
- Exact-main CI run #71: SUCCESS.
- Detailed closure record: `docs/phase-evidence/P00_CLOSURE.md`.

## P01 — Premium Application Shell & Design System — CLOSED

| Unit | Status | Closure evidence |
|---|---|---|
| P01::approved-brand-binary | CLOSED | Exact owner crest bytes are governed and pinned by byte length + SHA-256 `bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`; runtime validation remains fail-closed. |
| P01::shared-design-system | CLOSED | Shared Navy/Gold contract integrated across WPF and Web. |
| P01::desktop-premium-shell | CLOSED | Login, Dashboard, Library, Asset Details, New Ingest, Windows Tape Capture, Upload, Queue, Administration and Settings integrated. |
| P01::web-premium-shell | CLOSED | Responsive browser shell integrated for valid Web workflows and intentionally excludes Tape Capture. |
| P01::rtl-ltr | CLOSED | Desktop and Web rendered acceptance covers English LTR and Arabic RTL. |
| P01::responsive-adaptive-source-contract | CLOSED | Windows exact-size acceptance plus Web exact CSS viewports at 360/820/1440; Web horizontal-overflow gate enforced. |
| P01::states-accessibility | CLOSED | Demo badge, loading/empty/API error/permission/degraded treatments, keyboard focus and reduced-motion baseline integrated. |
| P01::automated-ui-contract-acceptance | CLOSED | `MAM.P01.UiAcceptance.Checks`, rendered Desktop acceptance and exact-viewport Web acceptance integrated and green. |
| P01::rendered-visual-acceptance | CLOSED | Final rendered evidence artifact digest `sha256:f8afe948bdadd43bfdb1972f58e8d504248745647efce4d04798634b4bb30adf`; owner acceptance recorded on PR #3 comment `5639106531`. |
| P01::integration-exact-main | CLOSED | PR CI #120 / `34634207504` SUCCESS; PR #3 merged; exact-main phase-exit CI #121 / `34634733889` SUCCESS on merge SHA `75889ba8a8bf07dc54ff885b1ab3111c7e849272`. |

### P01 integration evidence

- Validated PR head: `3f8dc8dddb5acd8f89f9581e3c5e741d84993c66`.
- PR #3 merged successfully.
- Merge SHA: `75889ba8a8bf07dc54ff885b1ab3111c7e849272`.
- Owner navigation/visual review governance: ACCEPTED.
- Detailed closure record: `docs/phase-evidence/P01_CLOSURE.md`.

## P02 — Central Identity, API, SQL Catalog — CLOSED

| Unit | Status | Closure evidence |
|---|---|---|
| P02::central-api-baseline | CLOSED | Central ASP.NET Core API, version/session/catalog/audit surfaces and health endpoints integrated through PR #9; PR CI #130 and exact-main CI #131 SUCCESS. |
| P02::sql-catalog-schema-migrations | CLOSED | SQL Server 2022 authoritative runtime, migration runner and clean-database repeat-safe initialization passed in PR #9 and exact-main #131; accepted migrations `0001_p02_core_catalog.sql` and `0002_p02_metadata_schema.sql`. |
| P02::identity-auth-abstraction | CLOSED | ADR 0005, stable roles/permissions, ASP.NET authentication boundary and Development+Local-only fixture integrated; non-development remains fail-closed. |
| P02::server-authorization | CLOSED | Executable acceptance proves anonymous catalog 401, Viewer write/audit 403 and authorized Editor/Admin operations server-side. |
| P02::asset-identity-lifecycle | CLOSED | Authoritative GUID asset identity, lifecycle, timestamps and explicit numeric versioning integrated in Domain/SQL/API. |
| P02::metadata-schema-templates | CLOSED | `core-media-v1` bilingual metadata schema/template baseline, SQL seed and protected discovery/validation API integrated. |
| P02::audit-foundation | CLOSED | SQL-backed persistent audit records create/update/conflict actor/action/entity/outcome evidence and protected audit reads. |
| P02::health-readiness | CLOSED | `/health/live`, `/health/config` and `/health/ready` integrated; SQL-ready behavior and deliberate SQL dependency degradation to HTTP 503 tested. |
| P02::optimistic-concurrency | CLOSED | SQL-backed `ExpectedVersion` semantics reject stale writes with conflict and increment current-version writes; cross-client visibility verified. |
| P02::desktop-api-integration | CLOSED | Windows Desktop Media Library uses the shared Central API client when configured, with no direct SQL path and explicit connected states. |
| P02::web-api-integration | CLOSED | Web Portal uses its server-side Central API proxy/client contract; actual `MAM.Web` executable read/write acceptance passed. |
| P02::shared-catalog-state | CLOSED | Independent `WindowsDesktop` and `WebPortal` client identities plus actual Web proxy observe the same authoritative SQL-backed catalog state. |
| P02::connected-ui-regression | CLOSED | Loading/Empty/API error/Permission denied/Degraded states preserved; Arabic RTL, English LTR, premium responsive Windows/Web rendered regression passed on exact-main #131. |
| P02::security-config-acceptance | CLOSED | Client DB-boundary scan, repository secret baseline and dependency vulnerability baseline all passed; SQL credentials remain server-side secret-resolved only. |
| P02::integration-exact-main | CLOSED | PR #9 head `cdaaf202acc2868244f7793ceb10610c2fe6c4fe`; PR CI #130 / `34639923576` SUCCESS; merge SHA `dee8f27fc000ddaf667fb9695922cbfcdfcd3316`; exact-main CI #131 / `34640169375` SUCCESS. |

### P02 integration evidence

- Implementation PR #9 merged successfully.
- Validated PR head: `cdaaf202acc2868244f7793ceb10610c2fe6c4fe`.
- Merge SHA: `dee8f27fc000ddaf667fb9695922cbfcdfcd3316`.
- PR CI run #130 / `34639923576`: SUCCESS.
- Exact-main phase-exit CI run #131 / `34640169375`: SUCCESS.
- Detailed closure record: `docs/phase-evidence/P02_CLOSURE.md`.

## P03 — Primary Storage & Durable Upload — CLOSED

| Unit | Status | Closure evidence |
|---|---|---|
| P03::primary-storage-adapter-contract | CLOSED | `IStorageObjectStore` + `FileSystemStorageObjectStore`; server-generated object keys, path confinement and Primary write/verify semantics integrated in PR #11. |
| P03::primary-storage-config-health | CLOSED | Primary target configuration and `/health/storage` readiness/degraded behavior exercised; injected unwritable Primary root returns explicit 503 degraded state. |
| P03::durable-upload-sessions | CLOSED | Migration `0003_p03_durable_upload.sql` persists sessions, chunk receipts and authoritative Primary-original records in SQL Server. |
| P03::chunked-resumable-protocol | CLOSED | Offset-based Central API protocol with independent chunk SHA checks; acceptance stops/restarts API after first 16 MiB and resumes from persisted offset. |
| P03::integrity-size-hash | CLOSED | Finalization measures server staging length/SHA-256, writes Primary, then re-verifies Primary size/hash before catalog promotion. |
| P03::path-file-validation | CLOSED | Filename-only normalization rejects traversal/path input; object keys remain server-generated and confined under configured Primary root. |
| P03::duplicate-policy | CLOSED | Existing authoritative SHA-256 returns deterministic conflict plus existing asset identity; tested in P03 acceptance. |
| P03::temporary-client-cache-boundary | CLOSED | Desktop/Web do not own permanent media; P03 client boundary scan proves no direct Primary adapter/credential path. |
| P03::desktop-upload-integration | CLOSED | Windows Upload workspace uses `MamUploadApiClient`, computes SHA-256, sends resumable chunks, resumes acknowledged offset and exposes progress/retry/degraded/permission states. |
| P03::web-upload-integration | CLOSED | Actual `MAM.Web` server-side proxy creates/chunks/finalizes uploads through the Central API; no Primary credential is exposed to browser code. |
| P03::interruption-recovery | CLOSED | Intentional API stop/restart preserves SQL session + server staging and resumes without restarting from zero; PR CI #139 and exact-main #140 SUCCESS. |
| P03::catalog-promotion-shared-visibility | CLOSED | Catalog asset is created only after verified Primary write; Windows-originated ingest is visible via Web/catalog identity and Web proxy ingest is visible via Central API. |
| P03::connected-upload-ui-regression | CLOSED | Existing P01 UI contract plus Windows/Web rendered acceptance passed on PR CI #139 and exact-main #140; explicit upload connected states are implemented. |
| P03::security-storage-boundary | CLOSED | `eng/p03-client-storage-boundary-acceptance.sh`, repository secret baseline and dependency vulnerability baseline all passed in #139/#140. |
| P03::integration-exact-main | CLOSED | PR #11 head `4ea53febbccac5fd10233f6618151251397c4988`; PR CI #139 / `34647039559` SUCCESS; merge SHA `8d48d0aae94f55953edabe06f26dd2c48bb8baba`; exact-main CI #140 / `34647242488` SUCCESS. |

### P03 integration evidence

- Implementation PR #11 merged successfully.
- Validated PR head: `4ea53febbccac5fd10233f6618151251397c4988`.
- Merge SHA: `8d48d0aae94f55953edabe06f26dd2c48bb8baba`.
- PR CI run #139 / `34647039559`: SUCCESS.
- Exact-main phase-exit CI run #140 / `34647242488`: SUCCESS.
- Detailed closure record: `docs/phase-evidence/P03_CLOSURE.md`.
- Production Primary Storage endpoint/type/capacity/service identity remains site-specific and is not falsely claimed as accepted.

## P04 — Media Inspection, Proxies & Previews — CLOSED

| Unit | Status | Closure evidence |
|---|---|---|
| P04::ffprobe-inspection | CLOSED | FFprobe inspection through `SqlServerMediaProcessingService` persists authoritative technical metadata; runtime acceptance passed in PR CI #150 and exact-main #151. |
| P04::processing-profile-contract | CLOSED | Versioned built-in processing profiles for inspection/video/image/audio/PDF are integrated with deterministic profile identity. |
| P04::durable-processing-jobs | CLOSED | Migration `0004_p04_processing.sql` plus SQL processing service persist job state, leases, heartbeat, attempts, failures and completion. |
| P04::video-proxy-generation | CLOSED | Worker-side FFmpeg video proxy generation passed runtime acceptance; Primary original is never opened for write. |
| P04::image-thumbnail-preview | CLOSED | Worker-side image preview generation to verified JPEG derivative passed runtime acceptance. |
| P04::audio-preview-strategy | CLOSED | Worker-side audio preview generation to server-managed derivative passed runtime acceptance. |
| P04::pdf-preview-strategy | CLOSED | Verified PDF original is delivered through the authorized server preview boundary; acceptance verifies returned SHA-256. |
| P04::derivative-storage-identity | CLOSED | Derivative GUID/object key is deterministic from asset + profile/version + original SHA; generated derivative size/SHA are verified through Primary adapter. |
| P04::processing-retry-recovery | CLOSED | Intentional crash-after-lease exit 86, API restart, lease expiry and second-worker reclaim of the same durable job passed; explicit Failed + retry path also passed. |
| P04::processing-audit | CLOSED | Queue/completion/failure/retry audit actions are persisted and asserted by end-to-end acceptance. |
| P04::asset-technical-details | CLOSED | Windows/Web Asset Details retrieve live technical metadata through Central API contracts only. |
| P04::processing-queue-ui | CLOSED | Windows/Web Processing Queue is connected to authoritative SQL-backed job state with retry/error/degraded/permission handling. |
| P04::cross-device-preview | CLOSED | Actual `MAM.Web` proxy retrieves video derivative bytes through Central API and verifies the same derivative SHA-256 without storage credentials. |
| P04::connected-processing-ui-regression | CLOSED | P01 UI contract plus Windows/Web rendered acceptance remained green in PR CI #150 and exact-main #151. |
| P04::security-client-worker-boundary | CLOSED | `eng/p04-client-worker-boundary-acceptance.sh`, repository secret baseline and dependency vulnerability baseline passed in #150/#151. |
| P04::original-preservation-determinism | CLOSED | Acceptance verifies video/image/audio/PDF Primary SHA-256 before/after processing and same asset/profile yields same job/derivative identity. |
| P04::integration-exact-main | CLOSED | PR #13 head `f185a651cbe7fe3dea1b9f78603e56ec5ff7a1e8`; PR CI #150 / `34664627802` SUCCESS; merge SHA `c1b432061a936b34192a853ce2b863e7e96352be`; exact-main CI #151 / `34664757705` SUCCESS. |

### P04 integration evidence

- Implementation PR #13 merged successfully.
- Validated PR head: `f185a651cbe7fe3dea1b9f78603e56ec5ff7a1e8`.
- Merge SHA: `c1b432061a936b34192a853ce2b863e7e96352be`.
- PR CI run #150 / `34664627802`: SUCCESS.
- Exact-main phase-exit CI run #151 / `34664757705`: SUCCESS.
- Detailed closure record: `docs/phase-evidence/P04_CLOSURE.md`.
- Production proxy/preview quality profiles and site-specific processing capacity remain external inputs and are not falsely claimed as accepted.

## P05 — Search, Collections & Metadata Curation — ACTIVE

| Unit | Status | Evidence / remaining gate |
|---|---|---|
| P05::free-text-search | READY | Implement authoritative SQL-backed free-text search through Central API. |
| P05::filters-facets-pagination | READY | Implement deterministic filters/facets with bounded safe pagination. |
| P05::grid-list-library | READY | Connect Windows/Web grid/list library modes to shared live search state. |
| P05::collections | READY | Implement server-side collection identity, membership, authorization and shared visibility. |
| P05::categories-tags | READY | Implement normalized categories/tags with authoritative persistence and shared visibility. |
| P05::saved-filters-policy | READY | Implement saved filters only where product policy approves them; otherwise record the intentional policy decision without fabricated approval. |
| P05::bilingual-metadata-editing | READY | Connect Arabic/English metadata curation to authoritative schema validation through Central API. |
| P05::metadata-concurrency-audit | READY | Preserve optimistic concurrency and audit semantics during metadata curation. |
| P05::bulk-metadata-operations | READY | Implement permissioned validation-safe bulk operations with explicit partial-failure reporting and no silent overwrite. |
| P05::archive-restore-lifecycle | READY | Implement non-destructive authoritative archive/restore lifecycle basics without deleting/replacing Primary originals. |
| P05::search-curation-audit-security | READY | Add audit and permission-negative acceptance for search-sensitive curation operations. |
| P05::arabic-english-search | READY | Document and test Arabic/English search normalization and expected behavior. |
| P05::connected-curation-ui-regression | READY | Preserve premium responsive Arabic RTL/English LTR loading/empty/error/retry/degraded/permission states. |
| P05::security-client-boundary | READY | Prove Desktop/Web remain Central-API-only with no direct SQL/Primary/worker credential or process path. |
| P05::integration-exact-main | READY | Lawful convergence, phase-exit evidence and exact-main CI required before P05 closure. |

P05 is the single current phase. Site-approved metadata dictionaries/taxonomy vocabulary and saved-filter policy remain external inputs where applicable and must not be represented as accepted without real evidence.

`UNPUSHED_WORK=NONE`
