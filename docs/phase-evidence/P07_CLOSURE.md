# P07 Closure Evidence — Windows Tape Capture Vertical Slice

- **Phase:** `P07 — Windows Tape Capture Vertical Slice`
- **Engineering status:** `CLOSED`
- **Implementation PRs:** `#19`, `#21`
- **Final convergence PR head:** `059e06580239740dc1fff1e7f0536e6237a4de0a`
- **Final merge SHA:** `154fc3382726fa43b06cc97f507b085f1c6384be`
- **PR CI:** run `#198` / `34677776281` — `SUCCESS`
- **Exact-main CI:** run `#199` / `34677926773` — `SUCCESS`
- **Owner/site physical acceptance:** `DEFERRED_TO_P12 / OWNER_LAST` — **not PASS**

## Engineering capabilities closed

P07 now contains an isolated Windows capture runtime and vendor-neutral provider contract, device/profile discovery, fail-closed preflight, preview/audio/timecode contracts with explicit unavailable/error states, temporary ingest-cache recovery, record/stop/finalize state, dropped-frame/error evidence, SHA-256/length finalization, resumable Central API upload to authoritative Primary Storage, P06 Backup-protection visibility, restart/network recovery identity, and protection-gated local-cache cleanup.

The Windows client remains non-authoritative: it has no direct SQL, Primary Storage, Backup Storage or Worker implementation path. Professional capture remains excluded from Web/API/Worker runtime boundaries. Arabic RTL and English LTR Windows/Web regressions are green.

## Automated acceptance

The final PR-head and exact-main suites verified:

- clean build with zero errors;
- P00–P06 regressions;
- P07 non-hardware orchestration/recovery acceptance;
- P07 Windows-only capture/security boundary acceptance;
- repository secret baseline;
- dependency vulnerability baseline;
- rendered Windows and Web UI evidence.

## Deferred production/site acceptance

The following evidence intrinsically requires a real target site, physical equipment or authorized human acceptance and is carried to **P12 — Production Readiness & Handover** under the owner-last policy:

- exact approved tape deck/capture card/driver/runtime;
- vendor adapter binding for that approved SDK/runtime when supplied;
- sustained real-tape capture using the approved preservation profile;
- physical preview/audio/timecode behavior;
- real dropped-frame result against an approved threshold;
- target-workstation restart/network/device-interruption exercise;
- target Primary/Backup endpoints and physical-independence evidence;
- owner/site acceptance on the production workstation/hardware combination.

The prepared package `docs/phase-evidence/P07_REAL_HARDWARE_ACCEPTANCE.md` and `eng/p07-real-hardware-evidence.ps1` remain the required execution path. Deferral does not convert these items into PASS.

## Transition decision

P07 engineering work is **CLOSED** because every repository/cloud-actionable deliverable is implemented, integrated and green on exact main. Physical/site acceptance is explicitly transferred to P12 instead of blocking intermediate engineering phases or being falsely represented as complete.

`P08 — Enterprise Administration & Policy` is the next ACTIVE engineering phase once the governance transition record is merged/applied.

`UNPUSHED_WORK=NONE`
