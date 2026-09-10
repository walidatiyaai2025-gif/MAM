# Project Control — Diwan Al Amiri MAM

## Repository lock

Authoritative repository: `walidatiyaai2025-gif/MAM`

All project work, plans, code, tests and release evidence belong to this repository unless an explicit architecture decision states otherwise.

## Source of truth order

1. Live repository state on `main`
2. `CURRENT_PHASE.md`
3. Master delivery tracker issue
4. `docs/IMPLEMENTATION_PLAN.md`
5. Architecture/settings/branding documents
6. Historical reference package/specifications
7. Chat/history

Historical reference material is input, not authority when it conflicts with the centralized architecture recorded in this repository.

## Fixed product constraints

- Central multi-user MAM.
- Windows Desktop + responsive Web Portal.
- Central API/server and SQL Server catalog.
- Permanent media stored on server-managed Primary Storage.
- Independently configured Backup Storage with checksum verification.
- Windows supports both tape capture and file upload.
- Web supports file upload/search/admin but not professional tape capture.
- Temporary local ingest cache is recovery protection, not permanent archive.
- Full Diwan Al Amiri branding.
- Arabic RTL + English LTR.
- Premium responsive UI quality is required throughout implementation.

## Execution rules

Before implementation work:
- fetch exact live `main`;
- inspect current phase, open PRs/issues and active work;
- recover legitimate in-progress work before creating duplicates;
- preserve unrelated valid work;
- do not weaken storage/security/protection invariants for convenience.

Before merge:
- branch must be based/reconciled with current main;
- relevant automated tests must pass;
- UI work must satisfy responsive + RTL/LTR evidence where applicable;
- settings added/changed must be documented;
- migrations/storage changes require recovery/compatibility consideration;
- no secrets or production credentials may be committed.

## Phase discipline

There is exactly one current phase in `CURRENT_PHASE.md`.

Work may prepare future interfaces only when necessary to close the active phase, but a future phase must not be declared complete early.

A phase transition requires its exit gate and closure evidence. External/owner-only dependencies are recorded explicitly and never converted into fake PASS evidence.

## Definition of done for a task

A task is CLOSED only when:
- implementation/documentation is committed;
- relevant tests/evidence exist;
- no known exact-main regression is introduced;
- the implementation is integrated into the intended branch/main state;
- configuration/documentation remains synchronized;
- user-visible behavior has appropriate loading/error/permission states;
- security-sensitive behavior is enforced server-side.

## Versioning

Use semantic versioning for released product artifacts. Development builds should expose:
- product version;
- commit SHA;
- build timestamp/number;
- environment name.

Non-production environments must show a visible environment badge.

## Architecture-change policy

Changes to any of the following require an ADR in `docs/adr/`:
- client/server boundary;
- database technology;
- authoritative storage model;
- backup/protection invariant;
- capture hardware/provider abstraction;
- authentication architecture;
- media processing architecture;
- fundamental branding/product identity.

## Security and secrets

Never commit:
- passwords;
- API keys;
- database credentials;
- storage credentials;
- private certificates/keys;
- real personal/sensitive production media or metadata.

Use development-safe fixtures and secret references/environment providers.

## Release evidence

A release candidate must record:
- exact main SHA;
- version;
- CI status;
- Desktop installer name + SHA-256;
- Server/Web/Worker package names + SHA-256;
- database migration version;
- supported platform matrix;
- known limitations;
- UAT result;
- production dependency status.
