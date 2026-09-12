# P05 — Search, Collections & Metadata Curation — Closure Evidence

Status: **CLOSED**

P05 closed only after the implementation PR was green, merged, and the full phase-exit workflow succeeded again on exact `main`.

## Verified integration identity

- Implementation PR: **#15 — P05: search, collections and metadata curation**
- Validated PR head: `893ee5c4b4de4fc38a2a5e0b02e01b0d939c6cbf`
- PR CI: run **#159** / `34669007837` — **SUCCESS**
- Implementation merge SHA: `b808555f223b9db95398271f35bd30974ac745dc`
- Exact-main phase-exit CI: run **#160** / `34669816694` — **SUCCESS**

## Exit-gate mapping

1. **Representative authoritative assets are findable by free text and filters/facets — PASS.**
   - SQL-backed curation search is exposed only through the Central API.
   - Automated acceptance creates authoritative representative assets and verifies English text, Arabic-normalized text, category, tag, collection and lifecycle search paths.

2. **Search is deterministic, safely paginated and shared across clients — PASS.**
   - Search requests enforce bounded page/page-size semantics and deterministic ordering.
   - Acceptance verifies independent pages return distinct stable results and the actual Web server observes the same SQL-backed state through its Central API proxy.
   - Windows and Web use the shared `MamCurationApiClient`/Central API contract rather than local-authoritative search logic.

3. **Arabic and English search behavior is documented and exercised — PASS.**
   - `CurationTextNormalizer` provides invariant normalization, including Arabic alef/hamza variants, ya/alif-maqsura, ta-marbuta and tatweel handling.
   - CI acceptance verifies English `reception` lookup and Arabic normalized `الاماره` lookup against authoritative bilingual metadata.

4. **Collections/categories/tags persist authoritatively and are shared — PASS.**
   - Migration `0005_p05_search_curation.sql` persists asset metadata, normalized tags, collections and collection membership in SQL Server.
   - Collection membership is versioned and stale writes fail with conflict rather than silently overwriting newer state.
   - Categories/tags participate in authoritative search/facet behavior and Web shared-state acceptance.

5. **Bilingual metadata edits validate, preserve optimistic concurrency and audit — PASS.**
   - Arabic/English metadata writes validate through the authoritative metadata schema registry and Central API write authorization.
   - `ExpectedVersion` is required; stale writes return HTTP 409 with current state and successful writes advance the authoritative asset version.
   - Successful metadata changes emit persistent curation audit evidence.

6. **Bulk metadata operations enforce validation/authorization and explicit partial failure — PASS.**
   - Bulk requests are bounded and each item retains its own expected version and schema validation.
   - Acceptance submits one stale item plus one valid item and verifies one explicit conflict plus one success; no silent overwrite occurs.
   - Bulk curation emits audit evidence with the requested/succeeded/failed outcome.

7. **Archive/restore is non-destructive to Primary originals — PASS.**
   - Lifecycle mutation changes authoritative catalog state only.
   - Acceptance computes the representative Primary original SHA-256 before archive, after archive and after restore and verifies byte-for-byte parity throughout.

8. **Permission boundaries hold — PASS.**
   - Anonymous curation search fails server-side.
   - Viewer collection/lifecycle writes fail with HTTP 403 while authorized Editor writes succeed.
   - Existing catalog read/write policies remain the server authorization boundary for P05 operations.

9. **Premium responsive RTL/LTR and explicit connected states remain green — PASS.**
   - Windows rendered acceptance succeeded on PR CI #159 and exact-main #160.
   - Web rendered acceptance succeeded on PR CI #159 and exact-main #160.
   - Connected P05 Windows/Web surfaces include search/filter, grid/list, metadata, collections, lifecycle and explicit loading/empty/error/degraded/permission/conflict handling while preserving the Diwan Al Amiri Navy/Gold design system.

10. **Relevant build/tests/security and exact-main CI are green — PASS.**
    - Exact-main #160 passed build, P00/P01/P02/P03/P04 regressions, P05 runtime acceptance, P05 Central-API client boundary, repository secret baseline, dependency vulnerability baseline, Windows rendered acceptance and Web rendered acceptance.

## Acceptance defect repaired during P05

The first P05 runtime cycle exposed a CI harness defect rather than a product defect: the final Python audit assertion combined a heredoc and herestring, causing JSON to be parsed as Python source. The harness was corrected without weakening any assertion. PR CI #159 and exact-main #160 then passed the full P05 runtime and security acceptance.

## Delivered P05 capabilities

- authoritative SQL-backed free-text search;
- filters/facets and bounded deterministic pagination;
- Arabic/English search normalization;
- connected Windows/Web grid/list Media Library search state;
- bilingual per-asset metadata persistence and editing;
- authoritative schema validation and optimistic concurrency;
- explicit bulk partial-failure semantics;
- collections with versioned membership;
- normalized categories/tags and shared visibility;
- intentional saved-filter policy state: disabled until product/site policy explicitly approves persistence/sharing semantics;
- non-destructive archive/restore lifecycle;
- persistent curation audit events;
- Central-API-only client boundary acceptance;
- automated P05 runtime/security/rendered acceptance in CI.

## Scope not falsely claimed

- Site-approved metadata dictionaries and controlled taxonomy vocabulary remain external inputs and are not represented as approved by P05.
- Saved filters are intentionally not enabled because persistence/sharing policy has not been explicitly approved; this is a documented policy decision, not a missing hidden implementation claim.
- Backup Storage, independently verified second-copy parity and the `Protected` invariant remain P06 work.

P05 is closed. P06 may become the single ACTIVE phase only through the governance transition that references this closure record.

`UNPUSHED_WORK=NONE`
