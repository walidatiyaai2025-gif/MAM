# ADR 0007 — External tape digitization with MAM inventory and digitized-content ingest

- Status: Accepted
- Date: 2026-09-17
- Program: Phase Two — Tape Inventory & Digitized Content Ingest
- Supersedes for future product scope: ADR 0004 professional in-product tape-capture requirement

## Context

The approved operating model has changed: physical HDCAM/Betacam tape playback and digitization are performed outside MAM using external recording/capture equipment and software. MAM is not responsible for controlling Sony decks, capture cards, transport controls, live preview, record/stop, dropped-frame monitoring during acquisition, RS-422 control, or vendor capture SDKs.

MAM remains responsible for governing the physical tape inventory and the digital content once an externally digitized file is delivered for ingest.

## Decision

Phase Two establishes the following authoritative boundary:

1. A physical tape is registered in MAM before or independently of digitization and receives a durable tape identity such as `TAPE-000001`.
2. The tape record stores inventory metadata, physical location, legacy identifiers, tape format, physical condition, digitization status and audit history.
3. HDCAM, Betacam and later approved tape families are inventory/source-format metadata only. Exact deck/capture hardware is outside MAM product scope.
4. MAM may generate/print two physical labels for the same tape identity (case and tape) and may resolve the tape record from a barcode scan.
5. Content-sheet images/PDFs can be attached to the tape with preserved originals, ordering, validation and audit metadata. OCR, if added, is a reviewable draft and not authoritative without human confirmation.
6. External digitization produces one or more digital files. Those files enter MAM through the governed durable upload/ingest path and are linked to the originating tape.
7. Each accepted digitized ingest is represented as a digital copy instance, for example `TAPE-000001-D01`, `D02`, without implying that MAM performed the physical capture.
8. MAM performs post-ingest technical validation/QC on the received file as configured, then creates/links the authoritative MAM asset/version and downstream derivatives.
9. Clip/source provenance must remain traceable from final clip/asset back to the digitized copy and physical tape, including source ranges where applicable.
10. Search and permissions cover tape code, legacy number, content-sheet/index metadata, digital copies, final clip codes and related searchable metadata.

## Explicitly out of scope

Phase Two does not implement or require:

- Sony deck transport control;
- RS-422 / 9-pin control;
- capture-card SDK integration;
- live capture preview or audio meters;
- record/stop/finalize orchestration against physical tape hardware;
- dropped-frame evidence produced by the external capture workstation;
- certification of exact tape decks, capture cards or capture drivers as a MAM release gate.

External digitization teams may maintain their own acquisition evidence outside MAM. MAM may store supplied digitization/QC notes or documents, but it does not claim to have generated hardware-capture evidence it did not observe.

## Data and workflow consequences

The core Phase Two provenance chain is:

`Physical Tape -> Tape Inventory -> Content Sheets / Content Index -> External Digitization -> Digitized File Upload -> Digital Copy -> MAM Asset/Version -> QC -> Clips/Search`

Digitization status is distinct from physical condition. Recommended workflow states include `NotDigitized`, `SentForDigitization`, `DigitizedFileReceived`, `Uploaded`, `QcPending`, `QcApproved`, `Completed`, with a separate partial-digitization state where needed.

A repeat digitization does not create a new tape identity. It creates another digital-copy instance under the same tape.

## Relationship to existing capture code

Existing capture-provider abstractions and Phase One P07 implementation/evidence remain historical repository artifacts unless separately retired. They are not an authoritative requirement for Phase Two and must not drive new hardware-integration work.

No Phase Two feature may reintroduce a requirement for in-product tape capture without a new explicit architecture decision.

## Security and integrity

- Barcode identifiers are lookup identifiers, not authentication tokens.
- Scanning a barcode never bypasses authorization.
- Tape creation, metadata changes, document attachment, digital-copy ingest/linking, code allocation, label reprint and QC decisions are audited.
- Uploaded digitized files use existing durable ingest, integrity verification, storage, backup and permission invariants.
- Unknown metadata remains unknown; the system must not synthesize factual tape metadata.

## Consequences

- MAM becomes simpler to deploy because no professional capture hardware stack is required for Phase Two.
- Tape inventory and provenance become first-class catalog functions shared by authorized Desktop/Web workflows as appropriate.
- Production acceptance focuses on inventory, barcode/label workflow, document attachment, durable digitized-file ingest, provenance, QC, search, permissions, backup and recovery—not deck/capture-card certification.
