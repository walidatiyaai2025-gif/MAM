# P06 — Backup Storage & Protection Invariant — Activation Record

Status: **ACTIVE**

P06 becomes the single current phase only after P05 implementation PR #15 was merged and exact-main phase-exit CI run #160 / `34669816694` succeeded on `b808555f223b9db95398271f35bd30974ac745dc`.

P05 closure evidence: `docs/phase-evidence/P05_CLOSURE.md`.

Authoritative active-phase definition: `CURRENT_PHASE.md`.

The P06 task ledger is `docs/TASK_LEDGER.md`. Production Backup Storage endpoint/type/capacity/service identity, exact physical independence topology, capacity thresholds, integrity-check cadence and notification destinations remain site-specific external inputs where applicable. These inputs must not be represented as accepted without evidence, but they do not block cloud-actionable implementation of the Backup adapter contract, durable copy queue, checksum parity, protection-state invariant, retry/recovery, corruption/mismatch acceptance, dashboard/health surfaces and automated security/integration evidence.

Core invariant: an asset is never `Protected` merely because a copy job was queued or bytes were written. `Protected` requires the authoritative Primary record plus a separately verified Backup copy whose required size/SHA-256 verification succeeds.

Backup failure handling must never overwrite, delete or downgrade the validity of a known-good Primary original.

`UNPUSHED_WORK=NONE`
