# P00 Supplied Source Import Evidence

Date: 2026-09-10

The owner supplied two source/reference packages during P00.

## MAM reference package

`MAM_Reference_Code.zip`

- ZIP SHA-256: `a6bd0a9ecfe986a096756aed6aeb941ac23945ced7a036331c0d4189ceb66732`
- Reusable small sources are preserved under `reference/mam-local-v1/`.
- The package is reference material, not a complete production application.
- Its SQLite/local/Tauri assumptions do not override the centralized MAM architecture.

## UI source package

`mam-desktop-source.zip`

- ZIP SHA-256: `bf8f8be51bf87c7a40d8bc898492b99a06a788c5f375b5adea2796555293277e`
- 148 archive entries / 130 materialized files.
- React 19 + TypeScript + TanStack Start/Router + Vite + Tailwind.
- Buildable source-level UI reference with Dashboard, Library, Asset Details, Import, Queue, Collections, Storage, Settings, Activity and System screens.
- The source package itself identifies its repository/data layer and DesktopBridge as mocks; those are not accepted as production evidence.
- Exact per-file source fingerprint inventory is under `reference/mam-ui-source-v1/SOURCE_MANIFEST_SHA256.txt`.

## P01 handoff

P01 may reuse/adapt the UI package's information architecture and interaction patterns, but must implement the approved product:

- Diwan Al Amiri branding only;
- exact owner-supplied crest, visually unmodified;
- Navy + Gold design tokens;
- Arabic RTL + English LTR;
- responsive Web and adaptive Windows UI;
- Central API rather than in-memory/local-authoritative repositories;
- SQL Server central catalog;
- distinct Primary + Backup storage protection state;
- Windows-native Tape Capture workflow;
- Windows + Web file upload;
- no user-facing mock/Lovable/Tauri/local-only branding.

This evidence closes the prior missing-reference-package blocker. P00 phase closure still requires successful branch CI, merge, and successful exact-main CI.
