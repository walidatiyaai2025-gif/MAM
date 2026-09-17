# Implementation Plan — Diwan Al Amiri MAM

## Delivery strategy

Build a production architecture from day one, but deliver a visible, reviewable application early. Avoid spending multiple phases on invisible infrastructure before the owner can judge the product.

Every phase has executable acceptance evidence. A phase is not closed by documentation alone.

## P00 — Foundation & reference reconciliation

Goal: turn the repository into a governed buildable project baseline.

Deliverables:
- solution/repository structure;
- coding standards and contribution rules;
- versioning/build metadata;
- CI skeleton;
- environment/configuration binding;
- map existing MAM reference schema/contracts/specification into accepted/rejected/adapted decisions;
- ADRs for client/server, database, storage and capture boundaries;
- no secrets in source control.

Exit gate:
- clean clone builds;
- tests execute;
- configuration validation exists;
- architecture docs match code structure;
- reference package decisions recorded.

## P01 — Premium application shell & design system

Goal: the owner can see and navigate a real Diwan Al Amiri product immediately.

Deliverables:
- Desktop shell;
- Web shell;
- shared design tokens/brand contract;
- Arabic RTL / English LTR;
- responsive navigation;
- login screen shell;
- Dashboard shell with realistic demo states;
- Media Library shell;
- Asset Details shell;
- New Ingest landing;
- Tape Capture workspace shell on Windows;
- Upload workspace shell;
- Administration/Settings navigation;
- loading/empty/error states.

No fake claim of backend completeness: demo data is explicitly marked in development builds.

Exit gate:
- Windows: 1366x768, 1920x1080, high-DPI checked;
- Web: 360px, tablet, 1440px checked;
- Arabic/English checked;
- owner can visually review the intended final product flow.

## P02 — Central identity, API, SQL catalog

Goal: establish the authoritative multi-user server boundary.

Deliverables:
- ASP.NET Core API;
- SQL Server schema/migrations;
- users/roles/permissions baseline;
- authentication modes abstraction;
- asset identity/lifecycle;
- metadata schemas/templates;
- audit foundation;
- health endpoints;
- optimistic concurrency for metadata edits.

Exit gate:
- two different clients see the same catalog state;
- clients have no direct DB credential/access;
- unauthorized operations fail server-side;
- migrations and clean DB initialization pass.

## P03 — Primary storage & durable upload

Goal: production-grade file ingest into server-managed Primary Storage.

Deliverables:
- storage adapter contract;
- Primary Storage configuration/health;
- durable chunked/resumable upload sessions;
- SHA-256 verification;
- file validation and path normalization;
- duplicate detection policy;
- Windows Upload workflow connected to live API;
- Web Upload workflow connected to live API;
- upload recovery after client/server interruption.

Exit gate:
- large test media resumes after interruption;
- server verifies final size/hash;
- no permanent local asset dependency;
- same ingested asset appears in Desktop and Web library.

## P04 — Media inspection, proxies & previews

Goal: uploaded assets become searchable/viewable media assets.

Deliverables:
- FFprobe technical metadata;
- versioned processing profiles;
- video proxies;
- image thumbnails/previews;
- audio/PDF preview strategy where applicable;
- durable processing jobs;
- retry/recovery;
- asset detail technical tab;
- queue UI.

Exit gate:
- original remains untouched;
- derivative generation is deterministic/auditable;
- failed worker job can be retried/recovered;
- preview works from another device.

## P05 — Search, collections & metadata curation

Goal: make the archive operationally useful.

Deliverables:
- free-text search;
- filters/facets;
- grid/list library;
- collections/categories/tags;
- saved filters where approved;
- bilingual metadata editing;
- bulk-safe metadata operations;
- archive/restore lifecycle basics.

Exit gate:
- representative asset set can be found by expected metadata/filter paths;
- Arabic/English search behavior documented and tested;
- permission boundaries hold for bulk actions.

## P06 — Backup Storage & protection invariant

Goal: every important asset has an independently verified second copy.

Deliverables:
- Backup Storage adapter/configuration;
- copy queue;
- checksum parity verification;
- Protected/BackupPending/BackupFailed/Mismatch states;
- retry/backoff;
- periodic integrity-check framework;
- Storage & Backup admin dashboard;
- capacity/health alerts.

Exit gate:
- asset cannot become Protected before both verified copies exist;
- corruption/mismatch test is detected;
- backup outage does not destroy/overwrite valid primary content;
- recovery and retry evidence exists.

## P07 — Windows tape capture vertical slice

> Historical Phase One implementation. The future product requirement for in-product professional tape capture is superseded by ADR 0007 and the Phase Two operating model below. Existing P07 code/evidence may remain for compatibility/history, but Phase Two must not add Sony deck/capture-card integration unless a new explicit architecture decision restores that scope.

Goal: live professional ingest from approved capture hardware.

Deliverables:
- capture-provider abstraction;
- first certified hardware adapter;
- device/profile management;
- live preview;
- audio meters;
- timecode handling;
- tape metadata;
- local temporary ingest cache;
- disk/network preflight;
- record/stop/finalize;
- dropped-frame/error evidence;
- automatic durable upload to Primary after capture;
- recovery after app restart/network loss where physically possible.

Exit gate:
- sustained real-device test on approved hardware;
- zero/accepted dropped-frame result under agreed profile;
- captured original hashes correctly on Primary;
- second verified copy reaches Backup;
- workstation cache cleanup follows policy only after safe handoff.

## P08 — Enterprise administration & policy

Goal: administrators can operate the product without editing config files.

Deliverables:
- Users & Roles;
- Metadata Dictionaries/Templates;
- Capture Stations/Devices;
- Processing Profiles;
- Primary/Backup settings with secret-safe controls;
- retention/delete policy;
- audit viewer/export;
- system settings;
- branding settings/assets;
- notification/alert configuration;
- validation/test-connection workflows.

Exit gate:
- critical settings changes are permission-protected and audited;
- plaintext secrets are never redisplayed;
- invalid storage/auth changes fail closed;
- restart requirements are explicit.

## P09 — Reports, monitoring, resilience & disaster recovery

Goal: make operations supportable and recoverable.

Deliverables:
- operational reports;
- ingest throughput;
- queue failures;
- storage capacity/protection coverage;
- structured logs/correlation IDs;
- diagnostics bundle;
- DB backup/restore runbook and automation hooks;
- configuration/key backup procedures;
- integrity verification reports;
- restart/crash/stale-job recovery;
- dependency health dashboard.

Exit gate:
- tested restore in non-production environment;
- intentional worker/API restart does not corrupt asset state;
- no silent loss of failed/pending jobs;
- health dashboard accurately reflects injected failures.

## P10 — Security, performance & scale acceptance

Goal: validate enterprise production behavior.

Deliverables:
- threat-model review;
- authorization/IDOR negative suite;
- upload/file validation security suite;
- secret scanning;
- dependency scanning;
- performance/load tests;
- concurrent ingest tests;
- search performance tests;
- storage/worker saturation behavior;
- browser/Windows compatibility matrix;
- accessibility acceptance;
- Arabic/English regression suite.

Exit gate:
- no critical/high unresolved security defects;
- agreed concurrency/throughput targets met;
- responsive/RTL/LTR acceptance green;
- failure modes are explicit rather than silent.

## P11 — Packaging, deployment & UAT

Goal: produce deployable release candidates.

Deliverables:
- signed/versioned Desktop installer workflow;
- Server/Web/Worker deployment packages;
- SQL migration bundle;
- production configuration template;
- deployment validator;
- upgrade/uninstall data-preservation rules;
- operator/admin deployment guide;
- UAT scripts;
- release notes and checksums.

Exit gate:
- clean environment installation succeeds;
- upgrade preserves database/catalog/media references;
- uninstall does not delete authoritative media;
- installer/release artifacts match documented versions/hashes;
- UAT passes on agreed devices/browsers.

## P12 — Production readiness & handover

Goal: close remaining site-specific dependencies and authorize production.

Required owner/site evidence:
- official Diwan Al Amiri logo, icon, palette and fonts approved;
- production DNS/TLS;
- SQL production/backup/HA choice;
- Primary and Backup storage endpoints/capacity/permissions;
- network/firewall/NTP/DNS readiness;
- approved externally digitized source-file/preservation ingest profile for tape-derived media;
- production identity integration;
- retention/audit policy approval;
- disaster-recovery/RPO/RTO approval;
- UAT sign-off.

Exit gate:
- no unresolved critical production blocker;
- all deployment dependencies have evidence;
- exact release version and artifact hashes recorded;
- production go-live checklist signed off by authorized stakeholders.

---

# Phase Two — Tape Inventory & Digitized Content Ingest

**Status:** APPROVED FOR ENGINEERING  
**Architecture decision:** `docs/adr/0007-external-tape-digitization-inventory-ingest-boundary.md`

## Phase Two scope boundary

Physical tape playback, Sony deck control and recording/digitization are performed **outside MAM**. MAM starts with physical tape inventory/finding aids and resumes when an externally digitized media file is delivered for upload.

Authoritative flow:

`Physical Tape -> Tape Inventory -> Barcode -> Content Sheets / Content Index -> External Digitization -> Digitized File Upload -> Digital Copy -> MAM Asset/Version -> QC -> Clips/Search`

Sony HDCAM and Betacam are source/tape-format metadata. Phase Two does not require deck transport, RS-422, capture-card SDKs, capture preview, record/stop controls or physical capture certification.

## T2.1 — Tape inventory foundation

Goal: make every physical tape a first-class governed inventory record before digital media is required.

Deliverables:
- `TAPE-######` durable sequential identity;
- tape title/description and legacy number;
- configurable tape format including HDCAM/Betacam families actually approved by the owner;
- physical condition separate from digitization status;
- owner/department and notes;
- physical location fields such as room/cabinet/shelf/bin;
- unknown values remain explicitly unknown;
- create/edit/view permissions and audit events;
- SQL migration and API/domain contracts;
- bilingual Desktop/Web inventory views.

Exit gate:
- sequential allocation is concurrency-safe;
- unauthorized create/edit/read paths fail server-side;
- tape identity remains stable after edits;
- Desktop/Web display the same authoritative record.

## T2.2 — Barcode and labels

Goal: make physical retrieval and record opening fast and reliable.

Deliverables:
- Code 128-compatible tape barcode value based on `TAPE-######`;
- paired CASE/TAPE label output using the same tape identity;
- configurable label dimensions/printer profile;
- reprint preserves the original code;
- scan/resolve workflow opens the authorized tape record;
- case/tape mismatch review flow;
- label-print/reprint audit history.

Exit gate:
- real printer/scanner acceptance path documented;
- duplicate/reprint does not allocate a new tape code;
- barcode lookup never bypasses authorization.

## T2.3 — Content sheets and source documents

Goal: preserve the paper/scan finding aids that describe tape contents.

Deliverables:
- JPEG/PNG/PDF attachment from file/scan/camera workflow as applicable;
- preserve source original and page order;
- tape/document/page metadata, hash, size, actor and timestamps;
- pending/ready/failed attachment lifecycle;
- crash/retry recovery without duplicate ready documents;
- permission-controlled preview/download;
- backup/protection coverage for the attachments.

Exit gate:
- multi-page/multi-document tape records work;
- incomplete copy/validation cannot become Ready;
- restart/retry cannot create a false duplicate attachment.

## T2.4 — Content indexing and reviewed OCR

Goal: make tape contents searchable before or after digitization.

Deliverables:
- manual content items with title/description/person/event/date/notes;
- optional source time/range fields when known;
- optional OCR extraction as non-authoritative draft;
- explicit human review/acceptance before OCR text becomes authoritative metadata;
- bilingual entry/search behavior.

Exit gate:
- content items are searchable by expected metadata;
- unreviewed OCR cannot silently become trusted catalog data.

## T2.5 — External digitization workflow tracking

Goal: track operational status without pretending MAM controls the recording equipment.

Deliverables:
- separate digitization status from physical condition;
- states covering not digitized, sent externally, file received, partial, uploaded, QC pending, QC approved and completed;
- optional external supplier/team, handoff/reference and notes;
- status transition audit history;
- no automatic hardware-capture claims.

Exit gate:
- every status transition is auditable;
- partial/repeat digitization is represented without creating a new tape identity.

## T2.6 — Digitized file upload and digital-copy identity

Goal: ingest externally created video into existing governed MAM storage.

Deliverables:
- Upload Digitized Content action from the tape record;
- reuse durable/resumable upload, size/hash verification and Primary/Backup invariants;
- create digital-copy identity such as `TAPE-000001-D01`, `D02`;
- link each digital copy to the source tape and resulting MAM asset/version;
- repeat digitization creates another digital copy, not another tape;
- upload recovery and truthful failure states.

Exit gate:
- large digitized media survives interrupted upload;
- resulting asset is traceable to exactly the intended tape/digital-copy record;
- Primary/Backup integrity rules remain unchanged.

## T2.7 — Post-ingest technical QC

Goal: validate the received digitized file, not the external capture hardware/process.

Deliverables:
- readable/parseable media check;
- container/codec/resolution/frame-rate/duration/audio-stream inspection;
- checksum and file-integrity evidence;
- configurable QC notes/decision;
- `QcPending`, `QcApproved`, `QcRejected` behavior where applicable;
- optional advanced black/freeze/silence checks only when separately implemented and evidenced.

Exit gate:
- corrupt/unreadable files cannot be approved;
- QC approval is permission-protected and audited;
- MAM never fabricates dropped-frame/hardware evidence it did not observe.

## T2.8 — Tape to digital copy to clip provenance

Goal: keep every published/reused clip traceable to physical source.

Deliverables:
- relationship `Tape -> Digital Copy -> Asset/Version -> Clip`;
- one or more source ranges per clip when required;
- validate source in < out and against known duration;
- allow one clip to reference multiple source segments/copies/tapes where business rules permit;
- preserve high-res/low-res versions under the same logical clip identity.

Exit gate:
- a final clip can be traced back to its originating tape(s) and source range(s);
- invalid/out-of-duration source ranges fail closed.

## T2.9 — Classification and clip code generator

Goal: issue stable business codes only after approved classification/save.

Deliverables:
- configurable Arabic category name and Latin prefix;
- concurrency-safe category counters;
- examples such as `INT-######`, `VIS-######`, `TRV-######`, `CON-######` only after owner approval;
- allocate code transactionally on successful save/approval;
- lock or govern category changes after issue;
- re-versioning does not create a new logical code unless business rules explicitly require it.

Exit gate:
- no duplicate issued codes under concurrency/retry;
- failed transactions do not consume/pretend a successful code without an explicit void policy;
- unauthorized users cannot allocate codes.

## T2.10 — Unified search and tape details UX

Goal: make the physical and digital archive discoverable from one governed experience.

Deliverables:
- search by tape code, legacy number, title, description, content index, person, event, category, clip code and related approved metadata;
- result type clearly distinguishes Tape, Digital Copy, Asset and Clip;
- Tape Details tabs: Overview, Content Sheet, Content Index, Digital Copies, Clips, History;
- barcode scan resolves into the same authorized details view;
- source/provenance trail visible from clip back to tape;
- premium responsive Arabic RTL / English LTR behavior.

Exit gate:
- representative tape/clip records are discoverable through expected terms;
- restricted tape/content remains absent from unauthorized search/results/detail paths.

## T2.11 — Security, audit, backup and recovery

Goal: make Phase Two inherit existing enterprise invariants rather than creating a side database/workflow.

Deliverables:
- server-side authorization for every mutation/read requiring protection;
- audit events for create/edit/link/upload/QC/code/label-reprint operations;
- content-sheet and tape-derived file protection through existing storage/backup architecture;
- counters and provenance included in backup/restore validation;
- crash/retry/idempotency checks for sequence allocation, attachments and digital-copy registration.

Exit gate:
- restore does not orphan digital copies/clips from tapes;
- retry/crash cannot create duplicate tape/clip identities;
- no barcode, direct ID or upload path bypasses authorization.

## T2.12 — Phase Two acceptance and rollout

Goal: prove the whole tape-inventory-to-digital-content workflow with representative real operational material.

Acceptance must cover at minimum:
- sequential tape-code allocation;
- paired label generation and reprint;
- real scanner/printer workflow where site hardware is available;
- content-sheet multi-page upload and recovery;
- manual content indexing and reviewed OCR behavior if OCR is enabled;
- repeat digital copies under one tape;
- large/interrupted digitized-file upload;
- post-ingest QC pass/fail;
- tape/digital-copy/clip provenance including multi-source cases;
- classification/code concurrency;
- authorized/unauthorized search and code allocation;
- backup/restore and crash/idempotency integrity;
- Desktop/Web RTL/LTR and responsive regression.

Phase Two is complete only when the software implementation, migrations, APIs, UI, automated acceptance and exact-main regression evidence are integrated. Site-only physical printer/scanner evidence may remain explicitly owner-last until executed, but no hardware tape-capture certification is required.

---

# Cross-phase engineering rules

## Visible quality from P01

Do not defer responsive design, bilingual layout, loading/error states or premium visual quality to a final polish phase. Every feature inherits the design system and is acceptance-tested as it is built.

## No duplicate business logic

Desktop and Web use the same server API and business rules. Platform-specific code is limited to true platform capabilities such as local OS integration. Phase Two tape recording/digitization remains outside MAM.

## No local-authoritative shortcut

Developer convenience must never introduce a production architecture where SQLite/local folders become the authoritative catalog/media store.

## Real evidence

Mock/demo adapters are allowed for CI and early UI work but cannot satisfy real storage/production acceptance. Phase Two must not claim evidence for external tape-capture behavior MAM does not perform.

## Owner/site dependencies are explicit

Exact official branding assets, storage endpoints, network policy, retention rules, production identity, approved digitized-source ingest profiles and any site printer/scanner acceptance are external inputs. Work that does not depend on them should continue, but no phase may pretend the real evidence passed.
