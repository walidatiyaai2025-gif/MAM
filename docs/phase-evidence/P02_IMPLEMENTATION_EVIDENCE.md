# P02 — Central Identity, API, SQL Catalog — Implementation Evidence

Status: **ACTIVE — FIRST VERTICAL SLICE READY FOR CI**

This record is deliberately partial. It documents cloud-actionable work implemented for the first P02 vertical slice and lists the remaining P02 exit-gate work that must not be represented as complete.

## Implemented candidate

Branch: `worker/p02-central-api-catalog`

### Central API and security boundary

- `MAM.Api` now identifies itself as P02 and exposes liveness, configuration and catalog readiness health endpoints.
- ASP.NET Core authentication/authorization is the authoritative API security boundary.
- Stable internal permissions are defined for catalog read/write, audit read and administration.
- Protected endpoints enforce policies server-side.
- Development-only identities use `X-MAM-Dev-User` aliases only when `Environment.Name=Development` and `Auth.Mode=Local`.
- Outside that exact development condition, the development authenticator returns no identity; protected operations fail closed.
- Authentication architecture decision is recorded in `docs/adr/0005-authentication-authorization-boundary.md`.

### Asset/catalog vertical slice

- Domain `MediaAsset` establishes authoritative asset identity, lifecycle, timestamps and explicit numeric versioning.
- Catalog API supports list/get/create/title-update operations.
- Title updates require `ExpectedVersion`; stale updates return an explicit conflict instead of overwriting newer state.
- Development uses a process-shared central memory catalog strictly as non-production executable evidence.
- Non-development registration uses `UnavailableAssetCatalog`; it does not silently fall back to local/in-memory authority.

### Audit and health

- Authoritative development mutations append audit events with actor/action/entity/outcome data.
- Audit reads require audit permission.
- `/health/ready` reports catalog readiness and returns HTTP 503 when the authoritative catalog provider is unavailable.

### SQL Server schema start

`database/migrations/0001_p02_core_catalog.sql` establishes the initial SQL Server schema contract for:

- schema migration history;
- users;
- roles;
- user-role assignment;
- media assets with numeric optimistic version and SQL rowversion;
- audit events and operational indexes.

The SQL script is **not** yet accepted as runtime migration evidence. The SQL Server provider, secret-resolution boundary, migration executor and clean-database CI acceptance remain open.

## Automated acceptance added

`eng/p02-api-acceptance.sh` starts the real `MAM.Api` process in Development and requires:

1. anonymous catalog read => `401`;
2. Viewer catalog read => `200`;
3. Viewer catalog write => `403`;
4. Editor asset create => success;
5. another Viewer request observes the same central API catalog state;
6. stale update => `409`;
7. current-version update => success and version increment;
8. Viewer audit read => `403`;
9. Administrator audit read contains create/update mutation evidence;
10. readiness health reports the Development catalog explicitly as non-production evidence.

CI runs this acceptance in the Linux build/security job. Existing P00/P01 checks remain enabled.

## P02 gates still open

The following remain required before P02 can close:

- real SQL Server catalog runtime wired to the Central API;
- secret-reference resolution without exposing DB credentials to clients;
- executable migration runner and clean supported DB initialization evidence;
- SQL-backed optimistic concurrency and audit persistence acceptance;
- metadata schema/template implementation and validation;
- authoritative user/role persistence and supported authentication runtime beyond the Development fixture;
- Desktop live Central API integration;
- Web live Central API integration;
- evidence that two distinct clients observe the same SQL-backed catalog state;
- connected UI loading/empty/error/degraded/permission behavior in Arabic RTL and English LTR;
- P02 PR integration and exact-main CI.

No DevelopmentMemory result may be used as SQL Server production acceptance evidence.

`UNPUSHED_WORK=NONE`
