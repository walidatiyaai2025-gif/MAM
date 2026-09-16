# ADR 0006 — Visual Segment Indexing and Image Search

- Status: Accepted for implementation on the merge-locked visual-search initiative
- Date: 2026-09-16
- Governing charter: `docs/PROJECT_CHARTER_VISUAL_SEGMENT_SEARCH.md`

## Context

MAM already stores transcript/OCR text and time/page segments and exposes text search. The product now requires each eligible video transcript segment to carry a representative thumbnail, visual content to be indexed, and authenticated users to search for visually similar media/segments by uploading an image.

This changes the media-processing and search architecture and therefore requires an ADR under `PROJECT_CONTROL.md`.

## Decision

1. **Central API remains authoritative.** Web/Desktop never submit trusted embedding vectors and never access database/storage paths directly.
2. **Visual segments extend the existing discovery model.** A text segment keeps its source/time/page identity and receives a stable segment identifier plus optional thumbnail/visual-index metadata rather than introducing a disconnected parallel transcript store.
3. **Derivatives stay derivatives.** Segment thumbnails are processing derivatives stored under server-managed derivative storage. They are not authoritative originals and do not participate in the Primary/Backup original-protection invariant.
4. **Visual vectors are provider-neutral.** Persist provider, model, model version, dimensions, source checksum and vector payload/reference. Comparisons may only occur between compatible provider/model/version/dimension tuples.
5. **Server-derived indexing.** A configured `IVisualEmbeddingProvider` derives query/index vectors from image bytes on the server. Clients cannot provide vectors as authoritative input.
6. **Fail closed.** If no visual provider is configured/ready, image search returns an explicit unavailable state. It must never return fabricated similarity matches.
7. **Permission filtering is server-side.** Image search must enforce the existing media-kind `view` permission before returning asset or segment matches.
8. **Idempotency.** `(asset, segment/page/source, source checksum, provider, model version)` is the logical indexing identity. Re-running the same operation is safe. A model/version change creates/replaces the compatible active index deterministically without mixing dimensions during similarity comparison.
9. **Local/offline capability is first-class.** The architecture supports an embedded deterministic visual-feature provider suitable for Offline Demo and air-gapped installations. A future production ML/semantic provider can implement the same interface without changing API/UI contracts.
10. **Bilingual premium UX.** Asset Details and Search expose the capability in Arabic RTL and English LTR using the existing Diwan Al Amiri design system and navigation patterns.

## Visual provider baseline

The initial local provider is a deterministic, non-secret, server-side image descriptor intended for offline similarity retrieval. It produces a normalized fixed-dimension vector from decoded visual content and records its explicit provider/model version. It is not represented as face recognition or semantic identity inference.

A later approved provider may supply a learned visual embedding model under the same contract. Cross-model comparisons are forbidden.

## Data consequences

Production SQL gains durable segment identity, thumbnail metadata, visual-index rows and indexing status. Demo SQLite receives equivalent logical tables/columns. Asset deletion cascades/removes segment/index metadata while original-media protection behavior is unchanged.

## API consequences

New API operations cover:
- segment listing with visual state;
- authorized thumbnail streaming/lookup;
- image-search upload with bounded validation;
- visual-index status/reindex request where processing permission permits.

Existing text search remains backward compatible and may enrich hits with segment identity/thumbnail context.

## Processing consequences

Video processing may generate a representative frame at the segment midpoint (or provider-selected key frame), persist derivative metadata, compute the visual descriptor, and truthfully update progress/error state. Audio-only segments remain visual-null. Document OCR may use page thumbnails when available.

## Security consequences

- Uploaded query images are bounded, validated and processed transiently unless an explicit future feature saves them.
- No query vectors are trusted from clients.
- No provider credentials are stored in index rows.
- Thumbnail retrieval is permission checked by asset before bytes are returned.
- Storage paths are never disclosed to clients.

## Rejected alternatives

- **Client-side embeddings:** rejected because it breaks the Central API authority boundary and allows forged vectors.
- **One giant transcript/search table:** rejected because processing/versioning, derivative lifecycle and visual provider compatibility require separate durable concerns.
- **Merge partial schema/API/UI work independently:** rejected by the initiative charter because partial integration would leave user-visible and data-contract inconsistencies.
- **Fake/demo-only similarity results:** rejected because success must reflect real persisted/queryable visual descriptors.
