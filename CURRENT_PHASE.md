# Current Phase

**Phase:** P06 — Backup Storage & Protection Invariant  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Make every important authoritative asset eligible for an independently verified second copy without weakening the Primary-original boundary. P06 establishes durable Backup Storage copy/verification state, explicit protection lifecycle semantics, mismatch/outage recovery and operator visibility so an asset can never be represented as `Protected` before required verification succeeds.

P06 must preserve the architecture established in P02–P05: Central API/SQL remain authoritative for state, server/worker components own storage operations, clients never receive storage credentials, Primary originals are never overwritten by Backup failure handling, and Backup verification remains independent from client-local cache or UI state.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- P00 architecture ADRs and application contracts
- P01 accepted Diwan Al Amiri design system and rendered UI baseline
- P02 accepted Central API, SQL catalog, authorization, audit and shared client boundary
- P03 accepted Primary Storage and durable upload implementation
- P04 accepted media inspection, durable processing and server-mediated preview implementation
- P05 accepted search, collections and metadata curation implementation
- `docs/phase-evidence/P05_CLOSURE.md`

## P06 required work

- [ ] Backup Storage adapter contract/configuration with a server-managed target separate from the authoritative Primary role.
- [ ] Backup Storage health/readiness with explicit degraded behavior and no false `Protected` state.
- [ ] Durable SQL-backed copy queue/job state with lease/retry/recovery semantics.
- [ ] Server/worker copy execution from verified Primary originals to Backup without mutating Primary bytes.
- [ ] SHA-256 and length parity verification after Backup copy completion.
- [ ] Authoritative protection state model covering `BackupPending`, `Protected`, `BackupFailed` and `Mismatch` semantics.
- [ ] Fail-closed invariant: `Protected` is impossible until both required copies exist and verification succeeds.
- [ ] Retry/backoff and recovery after Backup outage, process restart, stale lease or transient copy failure.
- [ ] Corruption/mismatch detection proving Backup divergence is visible and never silently accepted.
- [ ] Periodic integrity-check framework for previously protected assets without fabricating production schedule policy.
- [ ] Storage & Backup administration/dashboard surfaces with counts/state/health visibility.
- [ ] Capacity/health alert state surfaced safely through Central API without exposing credentials/paths to clients.
- [ ] Audit evidence for copy queueing, verification, failure, mismatch, retry/recovery and protection-state transition.
- [ ] Windows/Web connected protection/status UI with Arabic RTL + English LTR, loading/empty/error/degraded/permission states and premium Diwan Al Amiri styling.
- [ ] Security boundary acceptance proving Desktop/Web have no direct Backup/Primary SQL/storage credential or worker/process access.
- [ ] Automated integration/negative/recovery/corruption/original-preservation acceptance and exact-main CI.

## P06 exit gate

P06 can close only when:

1. A representative verified Primary asset is copied to a separately configured Backup target through the server/worker boundary.
2. Backup size and SHA-256 are independently verified against the authoritative Primary/original record before protection promotion.
3. `Protected` cannot be reached before both required copies exist and required verification succeeds.
4. Injected Backup corruption/mismatch is detected, persisted and exposed as non-Protected state.
5. Injected Backup outage/failure leaves valid Primary content untouched and records explicit degraded/failure state.
6. Retry/recovery after transient failure or process interruption completes without silent loss or duplicate corruption.
7. Periodic integrity-check mechanics can re-verify protected assets and surface mismatch without fabricating site scheduling policy.
8. Storage/Backup dashboard and client-visible protection state are consistent through the Central API on Windows and Web, with explicit permission/degraded states.
9. Client/database/storage/worker credential boundaries and P01–P05 regressions remain green.
10. Relevant automated build/tests/security checks and exact-main CI are green before closure.

## Previous phase

P05 — Search, Collections & Metadata Curation is **CLOSED**. Closure evidence: `docs/phase-evidence/P05_CLOSURE.md`.

## Next phase

P07 — Windows Tape Capture Vertical Slice.
