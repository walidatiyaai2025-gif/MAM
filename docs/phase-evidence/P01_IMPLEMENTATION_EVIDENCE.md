# P01 — Premium Application Shell & Design System — Implementation Evidence

Status: **CLOSED / INTEGRATED**

This record covers the cloud-actionable implementation for P01. Final rendered, owner-review and exact-main closure evidence is recorded in `docs/phase-evidence/P01_CLOSURE.md`.

## Implemented candidate

- Recovered branch: `worker/p01-premium-shell`.
- Integrated by PR #3.
- Validated PR head: `3f8dc8dddb5acd8f89b9581e3c5e741d84993c66`.
- Merge commit: `75889ba8a8bf07dc54ff885b1ab3111c7e849272`.
- Exact owner-supplied crest bytes are stored as governed base64 source chunks in `src/MAM.Application/Branding/DiwanCrestData.*.cs`; runtime reconstruction is SHA-256 checked before rendering.
- Approved crest SHA-256: `bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`.
- Shared Navy/Gold contract: `src/MAM.Application/Branding/BrandTokens.cs`.
- Windows WPF shell: `src/MAM.Desktop/`.
- Responsive Web shell: `src/MAM.Web/wwwroot/`.
- Automated source/contract acceptance: `tests/MAM.P01.UiAcceptance.Checks/`.
- Rendered Desktop acceptance: `tests/MAM.P01.VisualAcceptance.Checks/`.
- Exact CSS viewport Web acceptance: `tests/MAM.P01.WebVisualAcceptance.Checks/`.

## User-visible coverage

Windows provides login, Dashboard, Media Library, Asset Details, New Ingest, dedicated Windows Tape Capture, Upload, Processing Queue, Administration, Settings, build/environment identity and explicit demo state. It supports full Arabic RTL / English LTR direction switching and exposes loading, empty, API-unreachable, permission-denied and degraded-service treatments.

Web provides the same institutional product identity and shared workflow surfaces that are valid in a browser. It intentionally does **not** expose Tape Capture. The Web implementation includes desktop/wide, tablet and compact/mobile responsive compositions, RTL/LTR direction switching, keyboard focus treatment, reduced-motion handling and explicit error/degraded states.

## Architecture / safety invariants preserved

- Desktop and Web remain clients; no SQL Server or permanent-storage credentials were introduced.
- No permanent local-authoritative media library was introduced.
- Web does not fabricate Windows-only Tape Capture.
- The crest is reconstructed byte-for-byte from the exact owner-approved PNG source and is not recolored or redrawn.
- Demo data is visibly identified as non-production.
- The UI does not claim that an asset is Protected unless its displayed demo protection evidence shows Primary verified, Backup verified and SHA-256 match.

## Automated checks

`MAM.P01.UiAcceptance.Checks` fails the build if the approved crest byte length/fingerprint changes, locked brand tokens disappear, required Desktop/Web navigation/state surfaces disappear, Web exposes Tape Capture, RTL/LTR support disappears, responsive breakpoints/focus/reduced-motion contracts disappear, or crest packaging is removed.

Rendered acceptance additionally verifies:

- Windows exact layouts at 1366×768 and 1920×1080 plus 150% DPI in English and Arabic;
- Web exact CSS viewports at 360, 820 and 1440 in English LTR and Arabic RTL;
- zero horizontal overflow at the Web acceptance viewports;
- non-trivial PNG evidence artifacts.

## Final acceptance evidence

- PR CI run #120 / `34634207504`: SUCCESS.
- Final PR rendered artifact digest: `sha256:f8afe948bdadd43bfdb1972f58e8d504248745647efce4d04798634b4bb30adf`.
- Owner review acceptance: PR #3 comment `5639106531`.
- PR #3 merged to `main`.
- Exact-main phase-exit CI run #121 / `34634733889`: SUCCESS.
- Closure record: `docs/phase-evidence/P01_CLOSURE.md`.

P01 is CLOSED. P02 is the lawful next phase.

`UNPUSHED_WORK=NONE`
