# Current Phase

**Phase:** P00 — Foundation & Reference Reconciliation  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Create a buildable, governed project baseline that implements the centralized MAM architecture without inheriting local-only assumptions from historical reference material.

## P00 required work

- [x] Establish solution/repository structure for Desktop, Web, API, Worker, Domain/Application/Infrastructure and tests.
- [x] Add coding standards and contribution rules aligned with repository governance and architecture boundaries.
- [x] Add CI that builds and tests from a clean checkout.
- [x] Add version/build metadata surfaced by deployables.
- [x] Add configuration binding + validation based on `docs/SETTINGS_REFERENCE.md`, including strict rejection of undocumented keys, null-safe parsing and fail-closed critical invariants.
- [x] Add development-safe configuration templates with no secrets.
- [x] Reconcile the supplied MAM reference package (`schema.sql`, contracts, media reference code and specification) into an explicit compatibility/decision matrix. See `docs/REFERENCE_RECONCILIATION.md` and `reference/mam-local-v1/`.
- [x] Record ADR for centralized client/server, SQL Server and permanent-storage model.
- [x] Record ADR for Desktop technology selection after WPF vs WinUI spike, preserving .NET 10 direction.
- [x] Record ADR for upload-session and storage-adapter contracts.
- [x] Record the Windows capture/provider boundary and temporary-cache-to-central-ingest handoff; establish the vendor-neutral application contract without claiming hardware implementation.
- [x] Add secret scanning / dependency baseline.
- [x] Add development README/run instructions after project skeleton exists.

## P00 exit gate

P00 can close only when:

1. A clean clone of exact `main` builds successfully.
2. Automated tests execute in CI.
3. Configuration validation fails clearly for missing critical settings.
4. No secret/production credential is present in repository content.
5. Code structure matches the centralized server/storage architecture.
6. Reference-package decisions are documented; no silent local-only architecture survives.
7. Exact-main CI is green.

Current gate state: **PENDING CI / INTEGRATION**. All presently identified P00 cloud-actionable implementation, governance, configuration hardening, capture-boundary and reference-reconciliation work is committed on `worker/p00-foundation-baseline`. GitHub Actions has not yet assigned a hosted runner to the latest PR jobs (`runner_id=null`, no steps executed); the same condition was observed on both Windows and Ubuntu runner labels, so this is recorded as an external runner-allocation blocker rather than a passed or failed test. Closure still requires successful latest-head PR CI, merge, then successful exact-main CI.

## Next phase

P01 — Premium Application Shell & Design System.

P01 must produce an owner-reviewable Desktop and Web experience early, including Dashboard, Media Library, Asset Details, New Ingest, Windows Tape Capture shell, Upload shell and Administration/Settings navigation in Arabic RTL and English LTR. The owner-supplied `mam-desktop-source.zip` is an approved source-level UI reference for P01 only; its local/Tauri/mock runtime assumptions are not authoritative.
