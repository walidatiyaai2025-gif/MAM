# Current Phase

**Phase:** P01 — Premium Application Shell & Design System  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Make the intended final Diwan Al Amiri MAM product visible and reviewable immediately on Windows and Web without pretending later backend phases are complete.

## Authoritative inputs

- `docs/BRANDING_UI_UX.md`
- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- owner-supplied Diwan Al Amiri crest and Navy + Gold identity
- owner-supplied `mam-desktop-source.zip` as an approved source-level UI/interaction reference only

The reference application's local/Tauri/SQLite/mock runtime assumptions are not production authority. Desktop and Web remain clients of the Central API architecture established in P00.

## P01 required work

- [ ] Premium Windows Desktop shell.
- [ ] Premium responsive Web shell.
- [ ] Shared design tokens and brand contract using Diwan Al Amiri Navy + Gold identity.
- [ ] Arabic RTL and English LTR first-class layouts.
- [ ] Responsive/adaptive navigation.
- [ ] Login screen shell.
- [ ] Dashboard with realistic development/demo states.
- [ ] Media Library shell.
- [ ] Asset Details shell.
- [ ] New Ingest landing page/workspace.
- [ ] Windows Tape Capture workspace shell.
- [ ] Windows/Web Upload workspace shell.
- [ ] Processing Queue navigation/shell where required for the visible product flow.
- [ ] Administration and Settings navigation.
- [ ] Loading, empty, error, permission-denied and degraded-state treatments.
- [ ] Visible non-production/demo indication where mock data is used.
- [ ] Accessibility and keyboard/focus baseline appropriate to each platform.

## P01 exit gate

P01 can close only when:

1. Windows is checked at 1366×768, 1920×1080 and high-DPI scaling.
2. Web is checked at 360px, tablet and 1440px widths.
3. Arabic RTL and English LTR are both checked.
4. Branding is consistently Diwan Al Amiri with the owner-supplied crest preserved without redraw/recolor.
5. The owner can navigate and visually review the intended final product flow.
6. Loading/empty/error/degraded states are represented instead of placeholder-only pages.
7. Relevant automated build/tests/CI are green on exact main before closure.

## Previous phase

P00 — Foundation & Reference Reconciliation is **CLOSED**. Closure evidence: `docs/phase-evidence/P00_CLOSURE.md`.

## Next phase

P02 — Central Identity, API, SQL Catalog.
