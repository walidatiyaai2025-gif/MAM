# Supplied UI source baseline — 2026-09-10

Source package: `mam-desktop-source.zip`

- SHA-256: `bf8f8be51bf87c7a40d8bc898492b99a06a788c5f375b5adea2796555293277e`
- Size: 5,358,140 bytes
- Archive entries: 148
- Materialized files: 130
- Stack: React 19 + TypeScript + TanStack Start/Router + Vite + Tailwind CSS

## P00 disposition

This package is approved as a **P01 source-level visual and interaction reference**, not as the production runtime architecture.

Useful reference areas include Dashboard, Media Library, Asset Details, Import, Processing Queue, Collections, Storage, Settings, Activity, System, bilingual/RTL support, media preview controls and responsive component composition.

The package's own README/deployment notes explicitly identify an in-memory data store and mock `DesktopBridge`, and describe a future Tauri/SQLite/local-storage direction. Those runtime assumptions are rejected by the centralized MAM architecture. Production Desktop/Web clients consume the Central API; SQL Server and Primary/Backup storage stay server-side; Windows tape capture remains a native WPF/hardware workflow.

The complete per-file SHA-256 inventory is recorded in `SOURCE_MANIFEST_SHA256.txt` so a later imported/extracted copy can be verified byte-for-byte before reuse.

## Branding adaptation required before user-facing reuse

- Replace generic `MAM Desktop` identity with Diwan Al Amiri naming.
- Use the exact owner-supplied crest without redraw/recolor.
- Replace generic blue tokens with authoritative Navy + Gold tokens.
- Remove user-facing `mock`, `Lovable`, `Tauri`, `local-only` and vendor/demo branding.
- Remove public CDN/font dependencies for the institutional/offline baseline.
- Preserve Arabic RTL + English LTR.
- Add Windows Tape Capture workspace and central Primary/Backup protection states.

No production acceptance may be claimed from the package's mock repositories, mock bridge or sample media.
