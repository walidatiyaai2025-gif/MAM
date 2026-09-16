# Repository execution contract

- Repository hard lock: `walidatiyaai2025-gif/MAM` only.
- Read `CURRENT_PHASE.md`, `PROJECT_CONTROL.md`, issue #1 and live PR/branch/CI state before writing.
- While `docs/PROJECT_CHARTER_VISUAL_SEGMENT_SEARCH.md` is ACTIVE / MERGE-LOCKED, every worker touching transcript segments, thumbnails, visual indexing, image search, related processing/search schema or UX MUST read and obey that charter plus `docs/adr/0006-visual-segment-indexing-image-search.md`; all implementation belongs to the single branch/PR named there and MUST NOT merge partially.
- Recover existing lawful work before creating a new implementation.
- Do not add a local-authoritative catalog or permanent workstation media library.
- Desktop and Web consume the Central API; clients never receive SQL Server credentials.
- Primary and Backup storage identities/configuration remain distinct.
- Never mark an asset `Protected` before independent SHA-256 verification of both copies.
- Never commit production credentials, secrets, private keys, certificates, or real sensitive media/metadata.
- Branding is Diwan Al Amiri only. Preserve the owner-supplied crest without redraw/recolor.
- Arabic RTL and English LTR are first-class requirements for user-visible work.
- Before stopping, push recoverable changes and leave `UNPUSHED_WORK=NONE`.