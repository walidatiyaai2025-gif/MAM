# Current Phase

**Phase:** P00 — Foundation & Reference Reconciliation  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Create a buildable, governed project baseline that implements the centralized MAM architecture without inheriting local-only assumptions from historical reference material.

## P00 required work

- [x] Establish solution/repository structure for Desktop, Web, API, Worker, Domain/Application/Infrastructure and tests.
- [x] Add CI that builds and tests from a clean checkout.
- [x] Add version/build metadata surfaced by deployables.
- [x] Add configuration binding + validation based on `docs/SETTINGS_REFERENCE.md`.
- [x] Add development-safe configuration templates with no secrets.
- [ ] Reconcile the supplied MAM reference package (`schema.sql`, contracts, media reference code and specification) into an explicit compatibility/decision matrix. **External package is not present in live repository; see `docs/REFERENCE_RECONCILIATION.md`.**
- [x] Record ADR for centralized architecture and permanent-storage model.
- [x] Record ADR for Desktop technology selection after WPF vs WinUI spike, preserving .NET 10 direction.
- [x] Record ADR for upload-session and storage-adapter contracts.
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

Current gate state: **NOT SATISFIED**. Foundation implementation is pending CI/integration and the named historical reference package is still external/unavailable.

## Next phase

P01 — Premium Application Shell & Design System.

P01 must produce an owner-reviewable Desktop and Web experience early, including Dashboard, Media Library, Asset Details, New Ingest, Windows Tape Capture shell, Upload shell and Administration/Settings navigation in Arabic RTL and English LTR.
