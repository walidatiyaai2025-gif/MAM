# Current Phase

**Program:** Phase Two — Tape Inventory & Digitized Content Ingest  
**Engineering tranche:** T2.2 — Barcode & Labels  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Continue the approved Phase Two operating model in which physical tape playback/recording/digitization occurs outside MAM, while MAM owns the governed physical-tape inventory and the downstream barcode, content-document, digitization-status, ingest, QC, provenance and search workflow.

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

## T2.1 — Tape Inventory Foundation — CLOSED

T2.1 is engineering-closed and integrated into `main`.

Closure evidence:
- implementation PR #64 merged to `main`;
- exact-main T21 Tape Inventory Acceptance run #34 succeeded on merge `b4d5a814bbf14fe5a2ae2c5f09fe6aa109db892a`;
- PR #78 extended the tape-management surface and re-ran T21 Tape Inventory Acceptance run #43 successfully on head `bf1bfff9bd8465a1c34c1844aeed296e57002654`;
- PR #78 merged as `1e8da019769d7da9e0f275cfa0bd780117d6699b`;
- the former T2.1 implementation/planning branches are all fully behind `main` with no commits ahead;
- detailed closure record: `docs/phase-evidence/T21_TAPE_INVENTORY_CLOSURE.md`.

The closed foundation includes:
- concurrency-safe `TAPE-######` identity allocation;
- tape title/description and legacy number;
- configurable tape formats;
- physical condition independent from digitization status;
- room/cabinet/shelf/bin and owner/department metadata;
- explicit unknown/null semantics;
- SQL Server + Demo SQLite persistence;
- server-side authorization, audit and optimistic concurrency;
- bilingual Arabic RTL / English LTR Web and Windows Desktop inventory;
- complete physical-tape CRUD and tape-format administration;
- no in-MAM physical tape recording/digitization.

## Current engineering tranche — T2.2 Barcode & Labels

T2.2 must make each physical tape quickly identifiable and resolvable without creating a second identity system.

Required implementation:
- Code 128-compatible barcode value based on the existing durable `TAPE-######` identity;
- paired CASE/TAPE labels that use the same authoritative tape identity;
- configurable label dimensions and printer profile;
- label preview/output suitable for the supported Windows/Web workflow;
- reprint preserves the original tape code and never allocates a replacement identity;
- scan/resolve opens the authorized tape record through the existing Central API;
- case/tape mismatch review flow where two labels do not resolve to the expected same tape;
- server-side authorization for barcode resolution and label-management actions;
- audited print/reprint activity;
- bilingual Arabic RTL / English LTR UI and deterministic automated acceptance.

## T2.2 exit gate

T2.2 is complete only when:

1. generated barcode values resolve deterministically to the intended existing tape;
2. label generation/reprint cannot allocate or mutate `TAPE-######` identity;
3. CASE/TAPE paired-label output is consistent for one tape record;
4. barcode/direct-code resolution cannot bypass server-side authorization;
5. print/reprint actions required by the product are auditable;
6. Web/Desktop behavior remains Central-API-based and bilingual;
7. deterministic repository acceptance and exact-head CI are green;
8. implementation is merged normally to `main` with no legitimate T2.2 integration work stranded.

Real target-site printer/scanner acceptance may remain explicit OWNER_LAST evidence until actual hardware is available; that does not convert repository engineering evidence into physical-site PASS.

## Phase Two planned sequence

After T2.2, continue in the sequence defined by `docs/IMPLEMENTATION_PLAN.md`:
- T2.3 Content Sheets and Source Documents;
- T2.4 Content Indexing and Reviewed OCR;
- T2.5 External Digitization Workflow Tracking;
- T2.6 Digitized File Upload and Digital-Copy Identity;
- T2.7 Post-Ingest Technical QC;
- T2.8 Tape -> Digital Copy -> Clip Provenance;
- T2.9 Classification and Clip Code Generator;
- T2.10 Unified Search and Tape Details UX;
- T2.11 Security, Audit, Backup and Recovery;
- T2.12 Phase Two Acceptance and Rollout.

## Phase One production-readiness note

The previous Phase One P12 owner/site production-readiness evidence is not reclassified as PASS by this transition. Any unresolved DNS/TLS, production SQL/storage, production identity, retention/DR, signing, target-site UAT, approved digitized-source ingest profile or authorized go-live evidence remains explicitly owner-last/deferred until genuine evidence exists.

Physical tape deck/capture-card certification is not a MAM production requirement under ADR 0007.
