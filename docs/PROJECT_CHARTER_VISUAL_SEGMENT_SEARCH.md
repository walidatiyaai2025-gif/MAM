# Project Charter Addendum — Visual Segment Intelligence & Image Search

**Status:** ACTIVE / MERGE-LOCKED  
**Repository:** `walidatiyaai2025-gif/MAM`  
**Authoritative branch:** `worker/p12-visual-segment-image-search`  
**Integration policy:** exactly one implementation PR from this branch to `main`; **DO NOT MERGE** until every closure gate in this document is complete and exact-head CI is green.

## Purpose

Deliver one complete, integrated media-intelligence capability across MAM so that transcript/OCR segments are visually navigable, each eligible video segment owns a representative thumbnail, visual content is indexed for later similarity retrieval, and users can search by text or image from the bilingual premium Web experience.

This initiative is intentionally cross-cutting. Partial integration is prohibited because schema, processing, indexing, API contracts, permissions, Web UX, Demo compatibility, documentation and regression gates must remain coherent as one release unit.

## Non-negotiable integration rule

1. All implementation for this initiative MUST remain on `worker/p12-visual-segment-image-search` and its single PR until the entire charter is closed.
2. Do not split the initiative into mergeable partial PRs.
3. Do not merge schema-only, API-only, worker-only, search-only, or UI-only subsets to `main`.
4. The PR may receive many commits, fixes and test iterations, but merge is allowed only after the **Full Closure Gate** is satisfied.
5. Another worker may help only by contributing to this same branch/PR or by producing review/evidence that is integrated into it; no duplicate competing implementation branch may be merged.
6. Existing unrelated P12 owner-last/site evidence remains separate and must not be falsely marked complete by this engineering initiative.
7. Existing production invariants remain mandatory: Central API authority, SQL Server production catalog, server-managed Primary/Backup storage, SHA-256 protection rules, server-side authorization, no committed secrets, Arabic RTL + English LTR, premium responsive UX.

## Required product scope

### A. Transcript / OCR visual segments

- Extend text segments with stable segment identity and visual linkage.
- Video transcript segments must support one representative thumbnail per segment. The default capture point is the temporal midpoint unless an extractor provides a better key frame.
- Document/OCR segments may link to rendered page thumbnails where available.
- Audio-only segments remain valid without a visual thumbnail and must render a deliberate audio placeholder/state rather than fake imagery.
- Segment UI must expose start/end time, transcript/OCR text, thumbnail state, processing state and direct seek/open actions.
- Clicking a transcript segment for a video must navigate/seek to its exact media time where the player supports seeking.

### B. Visual indexing

- Add a durable visual-index record for asset/segment thumbnails.
- Store provider/model/version/dimension and vector payload or provider reference in a provider-neutral contract.
- Visual indexing must be deterministic and idempotent for the same source checksum + model version.
- Reprocessing must replace/supersede stale vectors safely and must not leave mixed model dimensions in one comparison operation.
- Do not persist secrets or external API credentials in visual-index records.
- Image-search capability must fail closed when no configured visual embedding provider is available; no fake similarity success is allowed.

### C. Search by image

- Add an authenticated, permission-filtered image-search API.
- Query image input must be validated for supported image type, bounded size and non-empty content.
- Query embedding is produced server-side; clients never supply trusted vector values.
- Similarity results must include score, asset, segment/page/time context, thumbnail URL/state and enough metadata for navigation.
- Results must enforce media-view permissions server-side before returning matches.
- Search must support asset-level images and segment-level thumbnails.
- Existing text search remains intact and gains visual-segment context where a text hit maps to a segment.

### D. Web UX — Arabic RTL + English LTR

- Asset Details gains a first-class `Transcript & Visual Segments` experience.
- Each eligible segment card/row shows thumbnail, time range/page, transcript/OCR text, status and actions.
- Search gains explicit modes for Text and Image without breaking the existing search route.
- Image search supports drag/drop or file picker, query preview, clear/replace, loading/progress, empty state, validation errors and results.
- Result cards show visual match score, thumbnail, asset title, media type, time/page context and open/seek action.
- All new labels, validation messages, empty states, tooltips and status text must have Arabic and English values and respect RTL/LTR layout.
- Reuse the existing Diwan Al Amiri premium design system, typography, navigation behavior, spacing, responsiveness and accessibility conventions.

### E. Processing / storage

- Thumbnail generation and visual indexing are durable processing operations with truthful progress/status.
- Generated thumbnails are derivatives, not authoritative originals; they must never weaken Primary/Backup original protection rules.
- Thumbnail object identity must be deterministic from asset + segment/page + source checksum where practical.
- Cleanup/reprocessing must avoid orphaned metadata and broken visual-index references.
- The normal production path remains SQL Server + server-managed storage. Offline Demo behavior must remain functional and must not become dependent on SQL Server or Internet.

### F. Compatibility / impact review

Before merge, explicitly verify impact on:
- catalog and asset deletion;
- upload/media-kind permissions;
- processing queues and retry semantics;
- search and categories;
- transcript revisions;
- protection/backup invariants;
- Demo SQLite schema/provider behavior;
- Desktop/Web client boundary;
- installer/build/package flow;
- localization and navigation;
- existing P09/P10/P11/P12 acceptance gates.

## Full Closure Gate — all required before merge

The single initiative PR may merge only when ALL items below are true:

- [ ] Governance charter and architecture ADR committed and referenced by the PR.
- [ ] Production SQL migration added and idempotent/recoverable.
- [ ] Demo SQLite schema/provider updated for compatible behavior.
- [ ] Application contracts cover visual segments, thumbnails, embeddings and image-search results.
- [ ] Infrastructure implementation persists and queries visual index safely.
- [ ] Processing implementation can create/record segment thumbnails and visual-index state truthfully.
- [ ] API endpoints implement retrieval, thumbnail delivery/lookup and image search with server-side permission enforcement.
- [ ] Web proxy/client boundary supports the new endpoints without exposing server secrets or storage paths.
- [ ] Asset Details `Transcript & Visual Segments` UX is complete.
- [ ] Search-by-image UX is complete.
- [ ] Arabic RTL and English LTR are complete for every new user-visible string/state.
- [ ] Text search still works and returns segment context correctly.
- [ ] Image-search validation, no-provider, no-match and permission-denied paths are tested.
- [ ] Reprocessing/idempotency/model-version behavior is tested.
- [ ] Asset deletion/cleanup behavior for segment thumbnails/index rows is tested.
- [ ] Existing exact-head CI and applicable P09/P10/P11/P12 gates are green.
- [ ] Dedicated visual-segment/image-search acceptance gate is green on the PR exact head.
- [ ] PR is reconciled with then-current `main` immediately before final merge.
- [ ] Final impact review contains no unresolved critical/high regression.
- [ ] `UNPUSHED_WORK=NONE` and no legitimate related implementation remains outside this PR.

## Merge authorization rule

Until every checkbox in the Full Closure Gate is closed with real code/test evidence, this initiative is **NOT MERGEABLE BY POLICY**, even if GitHub technically reports `mergeable=true`.

Once all closure items are complete, run exact-head CI, reconcile with current `main`, re-run required gates, then and only then perform the normal merge and post-merge verification.
