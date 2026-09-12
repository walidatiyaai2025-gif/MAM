# P07 — Windows Tape Capture Vertical Slice — Activation Record

Status: **ACTIVE**

P07 becomes the single current phase only after P06 implementation PR #17 was merged and exact-main phase-exit CI run #183 / `34674494602` succeeded on `62c46b615925a8f958b094afd3b2e313f80d256a`.

P06 closure evidence: `docs/phase-evidence/P06_CLOSURE.md`.

Authoritative active-phase definition: `CURRENT_PHASE.md`.

The P07 task ledger is `docs/TASK_LEDGER.md`. Exact tape deck/capture-card/driver models, certified hardware matrix, agreed preservation/capture profiles and sustained real-device acceptance remain site/owner inputs where applicable. Those inputs must not be represented as accepted without physical evidence, but they do not block cloud-actionable implementation of the capture-provider abstraction, safe development/mock provider, device/profile management, Windows-only capture orchestration, live preview/meter/timecode contracts, temporary cache/recovery, preflight, finalize/handoff to Central API, and automated non-hardware acceptance.

Core capture boundary: professional tape capture is Windows-only. Web must not gain a fake capture path. Captured media may use workstation-local temporary ingest cache for recovery, but the workstation must never become the permanent authoritative media owner.

Safe handoff invariant: a captured original is not safely handed off merely because recording stopped. The finalized capture must be hashed and durably uploaded through the Central API to Primary Storage; Backup protection then follows the P06 invariant. Temporary cache cleanup may occur only according to policy after the required safe handoff evidence exists.

Real-device completion remains fail-closed: mock/simulated hardware can support implementation and CI but cannot satisfy the P07 exit gate requiring sustained approved-hardware capture and agreed dropped-frame evidence.

`UNPUSHED_WORK=NONE`
