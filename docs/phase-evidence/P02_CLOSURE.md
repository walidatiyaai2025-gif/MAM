# P02 — Central Identity, API, SQL Catalog — Closure Evidence

Status: **CLOSED**

P02 established the authoritative multi-user catalog boundary behind the accepted Windows and Web shells. The phase closed only after the integrated implementation passed the complete P02 gate on exact `main`.

## Integration identity

- Implementation PR: #9 — `P02: complete SQL catalog and Central API client integration`
- Validated PR head: `cdaaf202acc2868244f7793ceb10610c2fe6c4fe`
- PR CI: run #130 / `34639923576` — **SUCCESS**
- Merge SHA: `dee8f27fc000ddaf667fb9695922cbfcdfcd3316`
- Exact-main phase-exit CI: run #131 / `34640169375` — **SUCCESS**
- Exact-main Linux job: build/test/security + all P02 runtime acceptance — **SUCCESS**
- Exact-main Windows job: Desktop rendered regression + Web rendered regression — **SUCCESS**

## Exit-gate evidence

### 1. Shared authoritative catalog through the Central API — PASS

The SQL-backed acceptance uses SQL Server 2022 and proves that independent `WindowsDesktop` and `WebPortal` client identities observe the same catalog state through `MamCatalogApiClient` and the Central API. The actual `MAM.Web` executable is also launched against the live Central API, creates an asset through its `/client-api` proxy, and that asset is independently observed through the Central API catalog.

### 2. No direct client SQL credential/access path — PASS

`eng/p02-client-boundary-acceptance.sh` scans `MAM.Desktop` and `MAM.Web` and fails if either client contains SQL client packages, `SqlConnection`, database secret references, or direct Infrastructure/SQL project dependencies. Both clients are wired through the shared Central API client contract.

Database connection strings are resolved server-side from secret references. Ephemeral CI credentials are generated at runtime, masked, and are never committed.

### 3. Server-side authorization — PASS

Executable acceptance requires:

- anonymous catalog access => HTTP 401;
- Viewer catalog read => allowed;
- Viewer catalog write => HTTP 403;
- Viewer audit read => HTTP 403;
- Editor catalog write => allowed;
- Administrator audit read => allowed.

Development header identities remain restricted to `Environment.Name=Development` plus `Auth.Mode=Local`. Non-development does not silently enable that fixture.

### 4. SQL migrations and clean database initialization — PASS

CI starts an ephemeral SQL Server 2022 container and creates a clean `MamP02Ci` database. `MAM.P02.SqlAcceptance.Checks` applies the migration directory, verifies the schema, applies the same migrations again to prove repeat-safe execution, and then exercises the SQL-backed catalog.

Accepted migrations:

- `0001_p02_core_catalog.sql`
- `0002_p02_metadata_schema.sql`

The schema includes migration history, user/role baseline, authoritative media assets, audit events, metadata schemas and metadata fields.

### 5. Optimistic concurrency — PASS

SQL catalog title updates require `ExpectedVersion`. A stale update is rejected as conflict without overwriting the current row; a current-version update increments the authoritative version and is visible to another client.

### 6. Audit evidence — PASS

SQL-backed audit persistence records and acceptance verifies:

- `catalog.asset.created`
- `catalog.asset.title-updated`
- `catalog.asset.title-update-conflict`

Audit events include actor/action/entity/outcome context and are exposed only through the protected audit API.

### 7. Health and degraded dependency behavior — PASS

- `/health/live` distinguishes process liveness.
- `/health/ready` reports provider `SqlServer` and Ready when the authoritative catalog is reachable and migrated.
- Acceptance deliberately points the API to an unavailable SQL endpoint and requires HTTP 503 with an explicit Degraded SQL catalog result rather than false readiness.

### 8. Connected Desktop/Web UX contracts — PASS

Windows Desktop Media Library uses the Central API when `MAM_API_BASE_URL` is configured and presents live Loading, Empty, Permission denied, API error and Degraded states. Catalog creation is sent through the Central API only.

Web Portal uses the Central API through its server-side client proxy and presents the same explicit connected states, live catalog rows and catalog creation flow.

Existing Arabic RTL / English LTR, responsive layout, accessibility and Diwan Al Amiri Navy/Gold branding remained mandatory. Exact-main run #131 passed the Windows Desktop and Web rendered regression gates.

### 9. Exact-main automation/security — PASS

On `dee8f27fc000ddaf667fb9695922cbfcdfcd3316`, exact-main CI run #131 completed successfully with:

- clean restore/build;
- P00 foundation acceptance;
- P01 UI contract acceptance;
- P02 Development authorization/catalog acceptance;
- ephemeral SQL Server 2022 startup;
- clean SQL migration/catalog acceptance;
- SQL-backed Central API two-client acceptance;
- client database-boundary acceptance;
- repository secret baseline;
- dependency vulnerability baseline;
- Windows Desktop rendered acceptance;
- Web rendered acceptance.

## Metadata baseline

P02 includes `core-media-v1` with bilingual field definitions and validation for title, event date, category, tags and preservation notes. The schema is represented in both application contracts and SQL seed data and is discoverable through the protected Central API.

## Authentication scope note

P02 closes the architecture and enforcement baseline: stable roles/permissions, server authorization policies, authentication-mode abstraction, user/role SQL schema and a Development-only local fixture. Production identity provider integration is intentionally site-specific and remains a later production-readiness dependency; P02 does not claim Active Directory/OIDC production sign-off.

## Production deployment note

The SQL Server 2022 CI environment proves supported clean initialization and authoritative runtime behavior. It does not claim that the final Diwan production SQL topology, HA/backup choice, DNS/TLS, storage endpoints or production identity provider are configured. Those site-specific dependencies remain governed by later phases, especially P11/P12.

## Phase result

All P02 exit gates are satisfied by integrated, executable evidence on exact `main`. P02 is CLOSED and P03 — Primary Storage & Durable Upload may become the single ACTIVE phase.

`UNPUSHED_WORK=NONE`
