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
- exact tape decks/capture cards/drivers;
- source format/preservation profile approval;
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

# Cross-phase engineering rules

## Visible quality from P01

Do not defer responsive design, bilingual layout, loading/error states or premium visual quality to a final polish phase. Every feature inherits the design system and is acceptance-tested as it is built.

## No duplicate business logic

Desktop and Web use the same server API and business rules. Platform-specific code is limited to true platform capabilities such as capture hardware, local cache and OS integration.

## No local-authoritative shortcut

Developer convenience must never introduce a production architecture where SQLite/local folders become the authoritative catalog/media store.

## Real evidence

Mock/demo adapters are allowed for CI and early UI work but cannot satisfy real capture/storage production acceptance.

## Owner/site dependencies are explicit

Exact official branding assets, capture hardware, storage endpoints, network policy, retention rules and production identity are external inputs. Work that does not depend on them should continue, but no phase may pretend the real evidence passed.
