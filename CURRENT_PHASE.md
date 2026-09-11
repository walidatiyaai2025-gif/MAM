# Current Phase

**Phase:** P04 — Media Inspection, Proxies & Previews  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Turn successfully ingested Primary originals into inspectable and viewable media assets without modifying or replacing the authoritative original.

P04 must add deterministic, durable and auditable media inspection/derivative processing while preserving the centralized server architecture and the P03 Primary Storage integrity boundary.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- P00 architecture ADRs and application contracts
- P01 accepted Diwan Al Amiri design system and rendered UI baseline
- P02 accepted Central API, SQL catalog, authorization, audit and shared client boundary
- P03 accepted Primary Storage and durable upload implementation
- `docs/phase-evidence/P03_CLOSURE.md`

## P04 required work

- [ ] FFprobe-based technical metadata inspection through a server/worker boundary.
- [ ] Versioned processing-profile contract with deterministic profile identity.
- [ ] Durable processing-job persistence, leasing/heartbeat/state transitions and stale-job recovery as applicable.
- [ ] Video proxy generation without modifying the Primary original.
- [ ] Image thumbnail/preview generation.
- [ ] Audio preview strategy and implementation where applicable.
- [ ] PDF preview strategy and implementation where applicable.
- [ ] Derivative object identity/path rules that remain server-generated and storage-safe.
- [ ] SHA-256/size verification for generated derivatives where applicable.
- [ ] Processing retry/recovery with explicit failure evidence and no silent job loss.
- [ ] Audit events for inspection, processing, retry, failure and successful derivative generation.
- [ ] Asset Details technical-information surface connected to live data.
- [ ] Processing Queue UI connected to authoritative job state.
- [ ] Preview delivery usable from another client/device through the server boundary.
- [ ] Loading/empty/error/retry/degraded/permission states for technical/preview/queue workflows.
- [ ] Arabic RTL / English LTR and responsive/premium UI contracts preserved.
- [ ] Clients remain free of direct SQL, Primary Storage or worker-process credentials/access.
- [ ] Automated unit/integration/negative/retry/determinism/original-preservation acceptance evidence.

## P04 exit gate

P04 can close only when:

1. A representative uploaded asset is inspected and its technical metadata is persisted and retrievable through the authoritative server boundary.
2. The authoritative Primary original remains byte-for-byte untouched by inspection and derivative generation.
3. Video/image derivatives are generated from an explicit versioned processing profile and the same input/profile produces deterministic auditable derivative identity/state.
4. A failed/interrupted processing job can be retried or recovered without silent loss, duplicate corruption or invalid success state.
5. Processing/job state remains durable across worker/API restart where applicable.
6. Preview/thumbnail content can be retrieved from another client/device through the server boundary without direct Primary Storage credentials.
7. Asset Details technical data and Processing Queue show authoritative live state with explicit loading/empty/error/retry/degraded/permission treatment.
8. Desktop/Web preserve Arabic RTL, English LTR, premium responsive behavior and existing accepted P01–P03 regressions.
9. Client/storage/database/worker security boundaries remain intact and repository secret/dependency checks are green.
10. Relevant automated build/tests/security checks and exact-main CI are green before closure.

## Previous phase

P03 — Primary Storage & Durable Upload is **CLOSED**. Closure evidence: `docs/phase-evidence/P03_CLOSURE.md`.

## Next phase

P05 — Search, Collections & Metadata Curation.
