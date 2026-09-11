# Current Phase

**Phase:** P02 — Central Identity, API, SQL Catalog  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Establish the authoritative multi-user server boundary behind the already accepted premium Windows and Web product shells.

P02 must connect both clients to one Central API and one authoritative SQL Server catalog without introducing direct client database access, local-authoritative catalog shortcuts or duplicated business rules.

## Authoritative inputs

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- P00 architecture ADRs and application contracts
- P01 accepted shared brand/design system and user-visible workflow shells

## P02 required work

- [ ] ASP.NET Core Central API baseline.
- [ ] SQL Server authoritative catalog schema and migrations.
- [ ] Clean database initialization path.
- [ ] Users, roles and permissions baseline.
- [ ] Authentication-mode abstraction suitable for later production identity integration.
- [ ] Server-side authorization enforcement.
- [ ] Asset identity and lifecycle baseline.
- [ ] Metadata schemas/templates baseline.
- [ ] Audit foundation for authoritative mutations.
- [ ] Health/readiness endpoints for API and catalog dependencies.
- [ ] Optimistic concurrency for metadata edits.
- [ ] Windows client catalog/API integration.
- [ ] Web client catalog/API integration.
- [ ] Shared state evidence showing two clients observe the same authoritative catalog.
- [ ] Loading/empty/error/degraded/permission states preserved when connected to the live P02 API.
- [ ] Arabic RTL / English LTR and responsive/premium UI contracts preserved.
- [ ] Secret-safe configuration; no client DB credentials.
- [ ] Automated unit/integration/negative authorization/migration acceptance evidence.

## P02 exit gate

P02 can close only when:

1. Two different clients observe the same authoritative catalog state through the Central API.
2. Desktop and Web have no direct SQL Server credential or access path.
3. Unauthorized operations fail server-side, not only in the UI.
4. SQL migrations and clean database initialization pass from a supported clean state.
5. Metadata edits have explicit optimistic-concurrency behavior.
6. Audit evidence exists for authoritative mutations.
7. API/catalog health and degraded dependency behavior are observable and tested.
8. Arabic/English, premium responsive UI and explicit failure states remain intact for the connected workflows.
9. Relevant automated build/tests/security checks and exact-main CI are green before closure.

## Previous phase

P01 — Premium Application Shell & Design System is **CLOSED**. Closure evidence: `docs/phase-evidence/P01_CLOSURE.md`.

## Next phase

P03 — Primary Storage & Durable Upload.
