# P06 — Backup Storage & Protection Invariant — Closure Evidence

Status: **CLOSED**

P06 closed only after its implementation PR passed, merged, and the full phase-exit workflow succeeded again on exact `main`.

## Verified integration identity

- Implementation PR: **#17 — P06: complete Backup Storage protection invariant**
- Validated PR head: `6d9a31a61927f0ab3f384428efb96cd16eb4f889`
- PR CI: run **#182** / `34674357402` — **SUCCESS**
- Implementation merge SHA: `62c46b615925a8f958b094afd3b2e313f80d256a`
- Exact-main phase-exit CI: run **#183** / `34674494602` — **SUCCESS**

## Exit-gate mapping

1. **Representative verified Primary asset copied to separately configured Backup — PASS.**
   - Backup operations remain server/worker owned.
   - P06 acceptance uses a Backup target distinct from the authoritative Primary role and verifies a representative authoritative asset is copied through the governed worker boundary.

2. **Backup size and SHA-256 parity verified before promotion — PASS.**
   - The Backup copy is independently measured and hashed against authoritative original evidence.
   - Acceptance verifies expected length and SHA-256 before the asset reaches `Protected`.

3. **Fail-closed `Protected` invariant — PASS.**
   - `BackupPending`, `Protected`, `BackupFailed` and `Mismatch` are explicit authoritative states.
   - Acceptance proves queued work alone does not promote an asset to `Protected`; required copy and verification must succeed first.

4. **Injected Backup corruption/mismatch is detected and exposed as non-Protected — PASS.**
   - Acceptance deliberately corrupts the Backup copy, queues an integrity recheck and verifies authoritative `Mismatch` state with cleared verified evidence.
   - Controlled repair restores verified parity without modifying the Primary original.

5. **Injected Backup outage/failure preserves valid Primary content — PASS.**
   - Acceptance uses a deterministic unavailable Backup destination and verifies explicit `BackupFailed` state.
   - Primary SHA-256 remains identical throughout outage and recovery.

6. **Retry/recovery and process interruption semantics — PASS.**
   - Retry/backoff and explicit failure state are persisted.
   - A deliberate crash after Backup lease, lease expiry and subsequent worker reclaim complete successfully without silent loss or Primary corruption.

7. **Periodic integrity-check mechanics — PASS.**
   - Protected assets can be queued for durable re-verification.
   - The framework detects divergence and surfaces `Mismatch` without inventing a production cadence that has not been approved by the site.

8. **Storage/Backup dashboard and client-visible state are consistent — PASS.**
   - Central API protection summary, health and per-asset state are authoritative.
   - Windows and Web consume Central API contracts and preserve explicit loading/empty/error/degraded/permission states with Arabic RTL + English LTR and the Diwan Al Amiri Navy/Gold design system.

9. **Client/database/storage/worker security boundaries and P01–P05 regressions — PASS.**
   - Desktop/Web have no direct SQL, Primary/Backup storage credential/adapter, worker or process-launch path.
   - P00–P05 runtime regressions and Windows/Web rendered acceptance remained green in PR CI #182 and exact-main #183.

10. **Automated build/tests/security and exact-main CI — PASS.**
    - Exact-main #183 passed build, Foundation, P01–P06 runtime/boundary checks, repository secret baseline, dependency vulnerability baseline and rendered Windows/Web acceptance.

## Acceptance defects repaired during P06

P06 convergence exposed two legitimate harness/runtime interaction problems and they were repaired rather than bypassed:

- P06 initially shared the same ephemeral acceptance database used by earlier phases. Earlier-phase authoritative rows referenced temporary Primary files that no longer existed, which caused unrelated terminal Backup failures to be re-observed during P06 acceptance. P06 acceptance was isolated to a dedicated `MamP06Ci` database so its evidence is deterministic and phase-owned.
- Backup outage simulation based on filesystem permission changes was not deterministic on hosted runners. The acceptance test now uses an intentionally unavailable Backup destination, preserving the same fail-closed product invariant without depending on runner permission behavior.

No assertion protecting Primary preservation, checksum parity, corruption detection, outage handling, stale-lease recovery, client boundaries or secret redaction was weakened.

## Delivered P06 capabilities

- server-managed Backup Storage contract/configuration distinct from Primary role;
- Backup readiness/degraded health semantics;
- durable SQL-backed Backup copy jobs with lease/attempt/retry state;
- worker-side copy execution from verified Primary originals;
- SHA-256 and length parity verification;
- authoritative `BackupPending` / `Protected` / `BackupFailed` / `Mismatch` lifecycle;
- fail-closed protection promotion;
- outage, retry and stale-lease recovery;
- corruption/mismatch detection and controlled repair;
- periodic integrity recheck mechanics;
- Primary-original preservation under Backup failure;
- protection summary/health/admin API surfaces;
- Windows/Web connected protection UI and bilingual state handling;
- persistent protection audit evidence;
- Central-API-only client security boundary acceptance;
- automated P06 runtime, negative, recovery, corruption, preservation, security and rendered acceptance in CI.

## Scope not falsely claimed

- Production Backup endpoint/type/capacity/service identity and exact physical independence topology remain site-specific external inputs until real deployment evidence exists.
- Production capacity thresholds, integrity-check cadence and alert/notification destinations remain site policy inputs and are not represented as approved by P06.
- Real hardware capture evidence is not part of P06 and belongs to P07.
- Repository branch protection is an owner/repository-administration control; it is not converted into product PASS evidence by this phase.

P06 is closed. P07 may become the single ACTIVE phase only through the governance transition that references this closure record.

`UNPUSHED_WORK=NONE`
