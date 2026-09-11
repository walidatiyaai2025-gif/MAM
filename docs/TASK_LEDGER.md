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

- Validated PR head: `3f8dc8dddb5acd8f89b9581e3c5e741d84993c66`.
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

## P03 — Primary Storage & Durable Upload — ACTIVE

| Unit | Status | Evidence / remaining gate |
|---|---|---|
| P03::primary-storage-adapter-contract | READY | Implement server-managed Primary Storage abstraction without client-owned permanent media. |
| P03::primary-storage-config-health | READY | Add secret-safe Primary Storage configuration, validation and readiness/degraded health. |
| P03::durable-upload-sessions | READY | Persist upload-session identity/state through the authoritative server boundary. |
| P03::chunked-resumable-protocol | READY | Implement deterministic chunk protocol and resume from acknowledged offsets. |
| P03::integrity-size-hash | READY | Verify final byte length and SHA-256 server-side before accepting the Primary original. |
| P03::path-file-validation | READY | Normalize names/paths, reject traversal/unsafe input and enforce configured file validation. |
| P03::duplicate-policy | READY | Define and test deterministic duplicate handling. |
| P03::temporary-client-cache-boundary | READY | Preserve temporary local/cache semantics only; clients must not become authoritative media storage. |
| P03::desktop-upload-integration | READY | Connect Windows Upload workflow to the Central API durable upload protocol. |
| P03::web-upload-integration | READY | Connect Web Upload workflow to the Central API durable upload protocol. |
| P03::interruption-recovery | READY | Prove recovery/resume after intentional client/network/server interruption without silent data loss. |
| P03::catalog-promotion-shared-visibility | READY | Promote only verified Primary originals and prove resulting catalog visibility to both Desktop and Web. |
| P03::connected-upload-ui-regression | READY | Preserve premium RTL/LTR responsive progress/retry/error/degraded/permission states. |
| P03::security-storage-boundary | READY | Prove Desktop/Web have no direct Primary Storage credential/access path and storage secrets remain server-side. |
| P03::integration-exact-main | READY | Lawful convergence, phase-exit evidence and exact-main CI required before P03 closure. |

P03 is the single current phase. Production Primary Storage endpoint/type/capacity/service identity remains site-specific and must not be represented as accepted until real site evidence exists; cloud-actionable adapter/protocol/test work continues independently.

`UNPUSHED_WORK=NONE`
