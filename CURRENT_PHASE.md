# Current Phase

**Phase:** P03 — Primary Storage & Durable Upload  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Deliver production-grade file ingest into server-managed Primary Storage while preserving the centralized architecture established in P02.

P03 must make Windows and Web uploads durable, resumable and integrity-verified without allowing clients or workstation cache to become authoritative media storage.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- P00 architecture ADRs and application contracts
- P01 accepted brand/design system and user-visible shells
- P02 accepted Central API, SQL catalog, authorization, metadata/audit and shared client boundary
- `docs/phase-evidence/P02_CLOSURE.md`

## P03 required work

- [ ] Storage adapter contract suitable for server-managed Primary Storage.
- [ ] Primary Storage configuration and health/readiness validation.
- [ ] Durable chunked upload sessions.
- [ ] Resumable upload protocol and persisted session state.
- [ ] SHA-256 calculation and server-side final verification.
- [ ] Final size verification before asset promotion.
- [ ] Safe filename/path normalization and traversal prevention.
- [ ] Allowed-file validation/quarantine behavior according to configuration.
- [ ] Duplicate detection policy with deterministic behavior.
- [ ] Temporary client/cache semantics; no permanent local-authoritative media dependency.
- [ ] Windows Upload workflow connected to the live Central API.
- [ ] Web Upload workflow connected to the live Central API.
- [ ] Upload recovery after client/network/server interruption where the protocol allows.
- [ ] Catalog state synchronized after successful Primary Storage commit.
- [ ] Loading/progress/retry/error/degraded/permission states for connected upload workflows.
- [ ] Arabic RTL / English LTR and responsive/premium UI contracts preserved.
- [ ] Secret-safe storage configuration; no storage credentials in clients.
- [ ] Automated unit/integration/negative/path-safety/resume/hash acceptance evidence.

## P03 exit gate

P03 can close only when:

1. A representative large test media upload can resume after an intentional interruption without restarting from zero.
2. The server verifies final size and SHA-256 before the upload is accepted as the Primary original.
3. The authoritative original is stored through the server-managed Primary Storage adapter, not a client-local permanent path.
4. Windows and Web have no direct Primary Storage credential/access path.
5. Unsafe/traversal paths and invalid file inputs fail closed.
6. Duplicate policy is deterministic and tested.
7. Upload/session dependency failures expose explicit recoverable/degraded states without silent data loss.
8. The same successfully ingested asset appears through the Central API catalog to both Desktop and Web clients.
9. Arabic/English, premium responsive UI and explicit upload failure/progress states remain intact.
10. Relevant automated build/tests/security checks and exact-main CI are green before closure.

## Previous phase

P02 — Central Identity, API, SQL Catalog is **CLOSED**. Closure evidence: `docs/phase-evidence/P02_CLOSURE.md`.

## Next phase

P04 — Media Inspection, Proxies & Previews.
