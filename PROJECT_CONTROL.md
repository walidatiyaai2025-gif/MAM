# Project Control — Diwan Al Amiri MAM

## Repository lock

Authoritative repository: `walidatiyaai2025-gif/MAM`

All project work, plans, code, tests and release evidence belong to this repository unless an explicit architecture decision states otherwise.

## Source of truth order

1. Live repository state on `main`
2. `CURRENT_PHASE.md`
3. Master delivery tracker issue
4. `docs/TASK_LEDGER.md`
5. `docs/OWNER_LAST_POLICY.md`
6. `docs/IMPLEMENTATION_PLAN.md`
7. Architecture/settings/branding documents
8. Historical reference package/specifications
9. Chat/history

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

There is exactly one current engineering phase in `CURRENT_PHASE.md`.

Work may prepare future interfaces only when necessary to close the active phase, but a future engineering phase must not be declared complete early.

A phase transition requires all repository/cloud-actionable implementation for the phase, applicable automated acceptance, integration evidence, and exact-main regression verification.

### Owner-last production acceptance policy

Human/site/external dependencies that intrinsically cannot be produced or verified by repository/cloud execution are **not allowed to stall P07–P11 engineering progression** once every cloud-actionable requirement of the current phase is complete. These dependencies are transferred to `P12 — Production Readiness & Handover` as `OWNER_LAST / DEFERRED_EXTERNAL` with an exact acceptance action and evidence requirement.

Typical P12 owner-last dependencies include physical capture hardware/driver certification, target-site DNS/TLS/network/firewall/NTP readiness, production SQL/storage endpoints and credentials, production identity-provider binding, signing certificates, approved preservation/retention/RPO/RTO policies, site throughput targets, target-device/browser UAT, and authorized go-live sign-off.

Deferral is never PASS. P12 cannot close and the project cannot be represented as production-approved/go-live-complete until every required owner-last item has real evidence. Engineering closure of P07–P11 means the software, automation, configuration surfaces, validators, packaging and non-production acceptance needed to execute those final site actions are complete.

## Definition of done for a task

A task is CLOSED only when:
- implementation/documentation is committed;
- relevant tests/evidence exist;
- no known exact-main regression is introduced;
- the implementation is integrated into the intended branch/main state;
- configuration/documentation remains synchronized;
- user-visible behavior has appropriate loading/error/permission states;
- security-sensitive behavior is enforced server-side.

A site-dependent acceptance task may instead be marked `DEFERRED_TO_P12 / OWNER_LAST`; that status is not equivalent to CLOSED or PASS.

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

Never commit passwords, API keys, database credentials, storage credentials, private certificates/keys, or real personal/sensitive production media or metadata.

Use development-safe fixtures and secret references/environment providers. Administrative configuration must never redisplay resolved plaintext secrets.

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
- UAT status;
- production dependency status.

Repository/cloud package acceptance may be complete before target-site UAT. Target-site UAT remains P12 owner-last and must be reported as deferred until executed.
