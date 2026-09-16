# Project Charter Addendum — Visual Segment Intelligence & Image Search

**Status:** CLOSURE EVIDENCE RECORDED / MERGE-LOCKED PENDING DOCUMENTATION-HEAD REVALIDATION  
**Repository:** `walidatiyaai2025-gif/MAM`  
**Authoritative branch:** `worker/p12-visual-segment-image-search`  
**Integration policy:** exactly one implementation PR from this branch to `main`; **DO NOT MERGE** until every closure gate in this document is complete and the final documentation head has fresh exact-head green CI.

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

The completed impact review and exact workflow evidence are recorded in `docs/phase-evidence/P12_VISUAL_SEGMENT_IMAGE_SEARCH_EXECUTION.md`.

## Full Closure Gate — all required before merge

The single initiative PR may merge only when ALL items below are true:

- [x] Governance charter and architecture ADR committed and referenced by the PR.
- [x] Production SQL migration added and idempotent/recoverable.
- [x] Demo SQLite schema/provider updated for compatible behavior.
- [x] Application contracts cover visual segments, thumbnails, embeddings and image-search results.
- [x] Infrastructure implementation persists and queries visual index safely.
- [x] Processing implementation can create/record segment thumbnails and visual-index state truthfully.
- [x] API endpoints implement retrieval, thumbnail delivery/lookup and image search with server-side permission enforcement.
- [x] Web proxy/client boundary supports the new endpoints without exposing server secrets or storage paths.
- [x] Asset Details `Transcript & Visual Segments` UX is complete.
- [x] Search-by-image UX is complete.
- [x] Arabic RTL and English LTR are complete for every new user-visible string/state.
- [x] Text search still works and returns segment context correctly.
- [x] Image-search validation, no-provider, no-match and permission-denied paths are tested.
- [x] Reprocessing/idempotency/model-version behavior is tested.
- [x] Asset deletion/cleanup behavior for segment thumbnails/index rows is tested.
- [x] Existing exact-head CI and applicable P09/P10/P11/P12 gates were green together on validated implementation head `aebde1d204a116b483433e4aa90da0db23e367eb`.
- [x] Dedicated visual-segment/image-search acceptance gate was green on that same validated implementation head.
- [x] PR was reconciled with then-current `main`; at closure-evidence time `main` remained the PR base `f5b0b5abf179d309f0853cd342847bec628d73ee`. This MUST be re-read immediately before merge.
- [x] Final impact review contains no unresolved critical/high regression.
- [x] `UNPUSHED_WORK=NONE` and no legitimate related implementation remains outside this PR.

## Final documentation-head revalidation latch

The checkbox closure above records the proven implementation state immediately before the documentation-only closure commits. Those documentation commits create a new PR head and therefore do **not** waive exact-head policy. Before merge, all required workflows must be freshly green on the final documentation head, `main` must be re-read/reconciled, PR reviews/threads/mergeability must be re-read, and the merge must use expected-head protection.

## Merge authorization rule

Until the final documentation head has fresh exact-head green CI and the live merge latch above is satisfied, this initiative remains **NOT MERGEABLE BY POLICY**, even if GitHub technically reports `mergeable=true`.

Once the final documentation head is green, reconcile with current `main` one final time, mark PR ready, perform the normal merge with expected-head protection, and complete post-merge exact-main verification before declaring closure.
