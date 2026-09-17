# Current Phase

**Program:** Phase Two — Tape Inventory & Digitized Content Ingest  
**Engineering tranche:** T2.1 — Tape Inventory Foundation  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Implement the approved Phase Two operating model in which physical tape playback/recording/digitization occurs outside MAM, while MAM owns physical tape inventory, barcode/labels, content sheets/content indexing, digitization workflow tracking, upload of externally digitized files, post-ingest QC, tape-to-digital-to-clip provenance, classification/code generation and unified search.

The authoritative Phase Two architecture boundary is recorded in `docs/adr/0007-external-tape-digitization-inventory-ingest-boundary.md`.

## Hard scope boundary

MAM does **not** control Sony HDCAM/Betacam decks or perform professional tape capture in Phase Two.

Out of scope unless a future explicit ADR changes the decision:
- deck transport control;
- RS-422 / Sony 9-pin control;
- capture-card SDK integration;
- live capture preview/audio meters;
- record/stop/finalize against physical tape hardware;
- physical capture dropped-frame monitoring/certification.

Sony HDCAM/Betacam are inventory/source-format metadata. Digitized media files are produced externally and then uploaded into MAM through the governed ingest path.

## Authoritative workflow

`Physical Tape -> Tape Inventory -> Barcode -> Content Sheets / Content Index -> External Digitization -> Digitized File Upload -> Digital Copy -> MAM Asset/Version -> QC -> Clips/Search`

## Current engineering tranche — T2.1

T2.1 must establish the durable tape inventory foundation before downstream barcode/document/digital-copy workflows depend on it.

Required implementation:
- concurrency-safe `TAPE-######` identity allocation;
- tape title/description;
- legacy/old number;
- configurable tape format values with HDCAM/Betacam families represented only when approved/configured;
- physical condition separate from digitization status;
- physical location fields such as room/cabinet/shelf/bin;
- owner/department and notes;
- explicit unknown/null semantics rather than fabricated values;
- SQL Server migration and server-side domain/application/API contracts;
- permission-protected create/read/update behavior;
- audit events;
- bilingual Arabic RTL / English LTR Desktop/Web tape inventory views;
- deterministic automated acceptance and exact-main regression evidence.

## T2.1 exit gate

T2.1 is complete only when:

1. tape-number allocation is unique and concurrency-safe;
2. unauthorized create/read/update paths fail server-side;
3. tape identity remains stable after metadata edits;
4. physical condition and digitization status are independently modeled;
5. Desktop and Web read the same authoritative central record;
6. migrations support clean install and upgrade without damaging existing catalog/media data;
7. relevant automated tests, bilingual UI evidence and exact-head CI are green;
8. implementation is merged normally to `main` with no legitimate integration work left stranded.

## Phase Two planned sequence

After T2.1, continue in the sequence defined by `docs/IMPLEMENTATION_PLAN.md`: barcode/labels; content sheets; content indexing/reviewed OCR; external digitization tracking; digitized-file upload/digital-copy identity; post-ingest QC; provenance; classification/code generator; unified search/UX; security/audit/backup/recovery; final Phase Two acceptance.

## Phase One production-readiness note

The previous Phase One P12 owner/site production-readiness evidence is not reclassified as PASS by this transition. Any unresolved DNS/TLS, production SQL/storage, production identity, retention/DR, signing, target-site UAT, approved digitized-source ingest profile or authorized go-live evidence remains explicitly owner-last/deferred until genuine evidence exists.

Physical tape deck/capture-card certification is no longer a MAM production requirement under ADR 0007.
