# P10 Activation — Security, Performance & Scale Acceptance

- **Phase:** `P10 — Security, Performance & Scale Acceptance`
- **Engineering status:** `ACTIVE`
- **Activation base:** P09 implementation merge SHA `1654158775ca2a235191e7f76bab58068d084ac2`
- **P09 exact-main dedicated acceptance:** run `#8` / `34687271073` — `SUCCESS`
- **P09 exact-main full CI:** run `#224` / `34687271068` — `SUCCESS`

## Objective

Validate the security, performance, concurrency, saturation, compatibility, accessibility and bilingual behavior of the engineering product assembled through P09 while preserving all authoritative server/storage/platform boundaries.

P10 may use deterministic repository/cloud engineering baselines and generated representative data. It must not fabricate production load/SLA targets, final site capacity certification, external penetration-test approval or physical target-device/browser acceptance that requires real P12 evidence.

## Required engineering work

- threat-model review;
- authorization/IDOR negative acceptance;
- upload/file validation security acceptance;
- secret/dependency scanning;
- representative API/SQL performance and load acceptance;
- concurrent ingest acceptance;
- representative search performance acceptance;
- processing/storage/protection saturation/backpressure acceptance;
- CI-exercisable browser/Windows compatibility evidence;
- accessibility acceptance;
- Arabic RTL / English LTR responsive regression;
- client/storage/database/worker/platform boundary re-verification;
- exact-main P00–P09 regression plus P10 acceptance convergence.

## P10 exit gate

P10 engineering may close only when the acceptance evidence defined in `CURRENT_PHASE.md` is real and green on exact `main`, with unsupported/site-only cases explicitly deferred rather than marked PASS.

## Owner-last / P12 transfer

Production load/SLA targets, final target-site concurrency/capacity certification, external penetration testing where required, and the final physical workstation/browser matrix remain `DEFERRED_TO_P12 / OWNER_LAST` unless real evidence is available.

`UNPUSHED_WORK=NONE`
