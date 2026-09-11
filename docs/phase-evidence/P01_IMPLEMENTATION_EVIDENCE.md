# P01 — Premium Application Shell & Design System — Implementation Evidence

Status: **READY FOR CI / VISUAL EXIT-GATE ACCEPTANCE**

This record covers the cloud-actionable implementation for P01. It is not a substitute for the required rendered visual checks in `CURRENT_PHASE.md`.

## Implemented candidate

- Recovered branch: `worker/p01-premium-shell`.
- Exact owner-supplied crest bytes are stored as governed base64 source chunks in `src/MAM.Application/Branding/DiwanCrestData.*.cs`; runtime reconstruction is SHA-256 checked before rendering.
- Approved crest SHA-256: `bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`.
- Shared Navy/Gold contract: `src/MAM.Application/Branding/BrandTokens.cs`.
- Windows WPF shell: `src/MAM.Desktop/`.
- Responsive Web shell: `src/MAM.Web/wwwroot/`.
- Automated source/contract acceptance: `tests/MAM.P01.UiAcceptance.Checks/`.

## User-visible coverage

Windows provides login, Dashboard, Media Library, Asset Details, New Ingest, dedicated Windows Tape Capture, Upload, Processing Queue, Administration, Settings, build/environment identity and explicit demo state. It supports full Arabic RTL / English LTR direction switching and exposes loading, empty, API-unreachable, permission-denied and degraded-service treatments.

Web provides the same institutional product identity and shared workflow surfaces that are valid in a browser. It intentionally does **not** expose Tape Capture. The Web source includes desktop/wide, tablet and compact/mobile responsive compositions, RTL/LTR direction switching, keyboard focus treatment, reduced-motion handling and explicit error/degraded states.

## Architecture / safety invariants preserved

- Desktop and Web remain clients; no SQL Server or permanent-storage credentials were introduced.
- No permanent local-authoritative media library was introduced.
- Web does not fabricate Windows-only Tape Capture.
- The crest is reconstructed byte-for-byte from the exact owner-approved PNG source and is not recolored or redrawn.
- Demo data is visibly identified as non-production.
- The UI does not claim that an asset is Protected unless its displayed demo protection evidence shows Primary verified, Backup verified and SHA-256 match.

## Automated checks

`MAM.P01.UiAcceptance.Checks` fails the build if the approved crest byte length/fingerprint changes, locked brand tokens disappear, required Desktop/Web navigation/state surfaces disappear, Web exposes Tape Capture, RTL/LTR support disappears, responsive breakpoints/focus/reduced-motion contracts disappear, or crest packaging is removed.

The repository CI also continues to run the P00 foundation/configuration/security checks, repository secret baseline and dependency vulnerability baseline.

## P01 exit gate still requiring real evidence

The following cannot be converted into PASS by source inspection alone:

1. Windows rendered check at 1366×768.
2. Windows rendered check at 1920×1080.
3. Windows high-DPI rendered check.
4. Web rendered check at 360px, tablet and 1440px.
5. Rendered Arabic RTL and English LTR review for clipping/layout quality.
6. Visual confirmation that the exact crest remains undistorted in the final rendered surfaces.
7. Owner navigation/visual review of the intended product flow.
8. Successful PR CI and, after lawful integration, successful exact-main CI.

Until those checks exist, P01 remains ACTIVE and must not be represented as CLOSED.

`UNPUSHED_WORK=NONE` is required before any execution stop.
