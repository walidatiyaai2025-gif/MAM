# P07 — Windows Tape Capture Vertical Slice — Cloud Acceptance Evidence

Status: **ACTIVE — CLOUD-ACTIONABLE BASELINE INTEGRATED; REAL-HARDWARE EXIT GATE OUTSTANDING**

P07 is **not CLOSED** by this record. The phase exit gate explicitly requires sustained acceptance on approved physical capture hardware/driver/profile. Simulator/CI evidence cannot replace that owner/site evidence.

## Verified integration identity

- Implementation PR: **#19 — P07: capture runtime, recovery and non-hardware acceptance**
- Validated PR head: `bdfd129f68adb3587ac4727cc9f2e185c1250b33`
- PR CI: run **#186** / `34675392410` — **SUCCESS**
- Merge SHA: `53157f157d402ea1c656e5c0f9b46651c10060a4`
- Exact-main CI: run **#187** / `34675528131` — **SUCCESS**
- Exact-main CI includes P00–P06 regressions, P07 non-hardware capture orchestration acceptance, P07 Windows-only capture/security boundary acceptance, repository secret baseline, dependency vulnerability baseline, Windows rendered acceptance and Web rendered acceptance.

## Cloud-actionable capabilities integrated

- existing `ICaptureProvider` boundary preserved as the Windows professional-capture abstraction;
- deterministic `SimulatedCaptureProvider` exists for CI/development only and is explicitly prohibited as physical certification evidence;
- fail-closed device/profile/cache/capacity preflight diagnostics;
- explicit Central API unavailable/degraded preflight state rather than silent success;
- timecode normalization with invalid/unavailable handling;
- simulated operator audio-meter/timecode/status contract exercise;
- bounded temporary-cache recovery manifests with atomic persistence and restart re-read acceptance;
- record/stop/finalize simulator path producing a recoverable temporary original;
- finalized capture length and SHA-256 evidence before handoff;
- durable handoff descriptor carrying session/tape/hash/length/dropped-frame/timecode evidence;
- Windows-only platform/security scan proving professional capture runtime is not introduced into Web/API/Worker and Desktop does not gain direct SQL/Primary/Backup storage implementation paths;
- P07 checks are now mandatory in repository CI.

## What this record does not claim

The following remain **OWNER_LAST / DEFERRED_EXTERNAL** or otherwise require real approved hardware/site evidence and therefore block P07 closure:

1. Approved tape deck/capture card exact model, driver/runtime and first certified production hardware adapter.
2. Sustained capture on that approved physical device using an agreed preservation/capture profile.
3. Real-device live preview behavior.
4. Real-device audio-meter/channel behavior.
5. Real-device timecode acquisition and failure/unavailable behavior.
6. Physical dropped-frame/error evidence and an explicitly agreed acceptance threshold; no threshold is fabricated by CI.
7. End-to-end physical capture finalization followed by actual Central API durable upload to Primary Storage with authoritative hash/length parity.
8. The physically captured asset progressing through the P06 workflow to verified Backup protection against the required site Backup target.
9. Physical workstation restart/network/device-interruption recovery and cache cleanup under the approved operational policy.
10. Owner/site acceptance of the resulting Windows capture workflow on the target workstation/hardware combination.

## Phase transition decision

Because the real-hardware exit gate remains unsatisfied, **P07 remains the single ACTIVE phase**. **P08 — Enterprise Administration & Policy remains NEXT/PENDING and must not be activated or implemented as the current phase yet.**

The exact action that unlocks legal P07 closure is to execute and record the approved-hardware acceptance package on the target deck/card/driver/profile, including sustained capture, preview/audio/timecode evidence, dropped-frame result/approved threshold, finalized SHA-256/length, Central API → Primary promotion, P06 Backup protection and safe temporary-cache cleanup evidence.

`UNPUSHED_WORK=NONE`
