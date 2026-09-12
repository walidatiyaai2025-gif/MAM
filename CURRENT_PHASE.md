# Current Phase

**Phase:** P05 — Search, Collections & Metadata Curation  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Make the centralized archive operationally useful by allowing authorized users to find representative assets quickly, curate bilingual metadata safely, organize assets into collections/categories/tags, and perform controlled lifecycle actions without weakening the authoritative Central API/SQL/Primary Storage boundaries established in P02–P04.

P05 must deliver deterministic search and curation behavior through shared server contracts so Windows Desktop and Web Portal observe the same authoritative results and metadata state.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- P00 architecture ADRs and application contracts
- P01 accepted Diwan Al Amiri design system and rendered UI baseline
- P02 accepted Central API, SQL catalog, authorization, audit and shared client boundary
- P03 accepted Primary Storage and durable upload implementation
- P04 accepted media inspection, durable processing and server-mediated preview implementation
- `docs/phase-evidence/P04_CLOSURE.md`

## P05 required work

- [ ] Authoritative free-text search contract and SQL-backed implementation.
- [ ] Filters/facets with deterministic query semantics and bounded pagination.
- [ ] Grid/list Media Library connected to live search state in Windows and Web.
- [ ] Collections with server-side identity, membership and authorization.
- [ ] Categories and tags with safe normalized values and shared visibility.
- [ ] Saved filters where supported by the approved product policy; otherwise document the intentional policy decision without fabricating approval.
- [ ] Bilingual Arabic/English metadata editing through the Central API.
- [ ] Metadata schema validation, optimistic concurrency and audit preservation during curation.
- [ ] Bulk-safe metadata operations with explicit selection, authorization, validation, partial-failure reporting and no silent overwrite.
- [ ] Archive/restore lifecycle basics using authoritative asset state rather than destructive media deletion.
- [ ] Search/curation audit events and permission-negative acceptance.
- [ ] Arabic search normalization/behavior documented and tested alongside English behavior.
- [ ] Loading/empty/error/retry/degraded/permission states for search, collections and metadata workflows.
- [ ] Arabic RTL / English LTR and responsive/premium Windows/Web UI contracts preserved.
- [ ] Clients remain free of direct SQL, Primary Storage or worker credentials/access.
- [ ] Automated unit/integration/negative/concurrency/bulk/search acceptance evidence.

## P05 exit gate

P05 can close only when:

1. A representative authoritative asset set can be found through expected free-text metadata paths and filter/facet combinations.
2. Search results are deterministic, paginated safely, and shared consistently between Windows Desktop and Web Portal through the Central API boundary.
3. Arabic and English search behavior is documented and exercised by automated acceptance evidence.
4. Collections/categories/tags persist authoritatively and are visible consistently across clients.
5. Bilingual metadata edits validate against the authoritative schema, preserve optimistic concurrency, and emit audit evidence.
6. Bulk metadata operations enforce authorization/validation, report partial failures explicitly and do not silently overwrite newer state.
7. Archive/restore basics change authoritative lifecycle state without deleting or replacing the Primary original.
8. Permission boundaries hold for search-sensitive operations, metadata writes, collection changes, bulk actions and archive/restore actions.
9. Desktop/Web preserve premium responsive Arabic RTL / English LTR behavior plus explicit loading/empty/error/retry/degraded/permission states and existing P01–P04 regressions.
10. Relevant automated build/tests/security checks and exact-main CI are green before closure.

## Previous phase

P04 — Media Inspection, Proxies & Previews is **CLOSED**. Closure evidence: `docs/phase-evidence/P04_CLOSURE.md`.

## Next phase

P06 — Backup Storage & Protection Invariant.
