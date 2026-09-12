# Current Phase

**Phase:** P07 — Windows Tape Capture Vertical Slice  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Deliver the first governed Windows-only professional tape-capture vertical slice while preserving every architecture and protection invariant already closed in P02–P06. P07 connects approved capture hardware through the capture-provider boundary, exposes operator-safe preview/meter/timecode/device/profile state, records into temporary workstation ingest cache, performs preflight/finalization/recovery, and hands finalized originals to the Central API for durable Primary Storage ingest and subsequent verified Backup protection.

P07 must not create a local-authoritative shortcut: the Windows workstation may hold temporary recovery cache, but permanent authoritative media remains server-managed Primary Storage with P06 Backup protection. Web remains upload/search/admin only and must not expose professional tape capture.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- ADR 0004 capture boundary and existing `ICaptureProvider` contract
- P01 accepted Diwan Al Amiri design system and Windows Tape Capture shell
- P02 accepted Central API, SQL catalog, authorization, audit and shared client boundary
- P03 accepted Primary Storage and durable upload implementation
- P04 accepted media inspection/processing and preview implementation
- P05 accepted search/metadata curation implementation
- P06 accepted Backup Storage/protection invariant implementation
- `docs/phase-evidence/P06_CLOSURE.md`

## P07 required work

- [ ] Reconcile and harden the capture-provider abstraction for a Windows-only professional ingest runtime without leaking hardware logic into Web/server business rules.
- [ ] Implement the first hardware adapter behind the provider boundary where repository-accessible SDK/runtime evidence permits; retain a clearly identified safe mock/simulator for CI only.
- [ ] Device discovery, selection, connection state and capture-profile management with explicit unsupported/degraded/error states.
- [ ] Live preview contract/surface appropriate to the approved capture path.
- [ ] Audio meter state and operator-visible channel/level feedback where supported by the provider.
- [ ] Timecode acquisition/normalization and explicit unavailable/invalid timecode behavior.
- [ ] Tape/capture metadata capture and authoritative handoff fields.
- [ ] Temporary workstation ingest-cache lifecycle with bounded paths, recovery identity and no permanent-authoritative ownership.
- [ ] Disk/network/device preflight before recording, with fail-closed blocking conditions and operator-readable diagnostics.
- [ ] Record/stop/finalize state machine that preserves incomplete/interrupted capture evidence for recovery instead of silent loss.
- [ ] Dropped-frame/device/runtime error counters and persisted/transferable evidence.
- [ ] SHA-256/length finalization of captured originals before durable upload handoff.
- [ ] Automatic durable upload through the Central API to Primary Storage after successful capture finalization.
- [ ] Restart/network-loss recovery where physically possible without duplicating/corrupting finalized originals.
- [ ] P06 protection handoff visibility so captured originals can progress from authoritative Primary to verified Backup without bypassing protection rules.
- [ ] Windows premium Diwan Al Amiri capture UX with Arabic RTL + English LTR, keyboard/accessibility, loading/idle/preflight/recording/finalizing/uploading/recovery/degraded/error/permission states.
- [ ] Security boundary acceptance proving Web has no professional capture path, clients do not gain SQL/storage credentials, and capture/runtime processes cannot bypass Central API authoritative promotion.
- [ ] Automated non-hardware capture orchestration/recovery/security acceptance plus exact-main regression CI.
- [ ] Real approved-hardware acceptance package/checklist prepared for owner/site execution when physical deck/card/driver/profile inputs are available.

## P07 exit gate

P07 can close only when:

1. A sustained capture test succeeds on approved real hardware/driver using an agreed capture profile; simulator/mock evidence alone is insufficient.
2. Device/profile/preflight state is explicit and unsupported/degraded hardware conditions fail closed rather than silently recording with unknown quality.
3. Live preview, audio meter and timecode behavior are proven for the approved hardware path, including explicit unavailable/error handling where applicable.
4. Record/stop/finalize produces a recoverable finalized original with authoritative capture metadata and durable error/dropped-frame evidence.
5. Dropped-frame result is zero or within an explicitly agreed acceptance threshold for the approved profile; no fabricated threshold may be assumed.
6. Finalized capture length/SHA-256 are established before handoff and the captured original is durably uploaded through the Central API to Primary Storage.
7. The authoritative Primary copy preserves the finalized capture hash/length and becomes visible through normal catalog/client flows without permanent workstation dependency.
8. The captured asset progresses through the P06 protection workflow and reaches verified Backup protection when the required site Backup target is available.
9. Restart/network-loss recovery and temporary-cache policy are exercised; cache cleanup occurs only after the required safe handoff evidence exists.
10. Windows Arabic RTL/English LTR premium capture UX, security boundaries, P00–P06 regressions and relevant automated build/tests/security/exact-main CI are green.

## Previous phase

P06 — Backup Storage & Protection Invariant is **CLOSED**. Closure evidence: `docs/phase-evidence/P06_CLOSURE.md`.

## Next phase

P08 — Enterprise Administration & Policy.
