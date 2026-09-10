# Contributing to Diwan Al Amiri MAM

This repository implements an institutional, multi-user Media Asset Management platform. Changes must preserve the architecture and evidence rules in `PROJECT_CONTROL.md`, `CURRENT_PHASE.md`, `docs/ARCHITECTURE.md`, `docs/SETTINGS_REFERENCE.md`, and the active phase plan.

## Engineering baseline

- Target .NET 10 and keep nullable reference types enabled.
- Prefer explicit, testable application contracts over static/global state.
- Use async APIs for I/O and propagate `CancellationToken` through long-running operations.
- Treat compiler warnings, validation failures, unhandled exceptions, data-loss risks, and secret leakage as defects rather than acceptable operational states.
- Keep Domain free of infrastructure/framework dependencies; Application owns use-case contracts; Infrastructure implements server-side adapters; API is the authoritative network/business boundary.
- Desktop/Web clients never receive SQL Server credentials or permanent storage credentials and never become the authoritative catalog or media archive.
- Platform-specific Windows capture/OS integration stays behind explicit interfaces and must not leak vendor SDK types into Domain/Application contracts.

## Data, storage and integrity

- SQL Server is the authoritative production catalog.
- Primary Storage is the authoritative media target; Backup Storage is independently configured and is never a silent normal-write fallback.
- Original media is immutable after successful ingest unless a later governed workflow explicitly replaces a version.
- SHA-256 is the baseline content-integrity algorithm.
- `Protected` is allowed only after Primary and Backup originals are independently readable and checksum-verified with matching SHA-256 evidence.
- Windows ingest cache is temporary recovery state only. Do not create a permanent workstation media library.
- Durable operations must be idempotent/recoverable where retries or process restarts are possible.

## Configuration and secrets

- Add or change material configuration only together with `docs/SETTINGS_REFERENCE.md`, typed binding, fail-closed validation, templates, and tests.
- Unknown configuration keys must not silently change or bypass security/storage behavior.
- Never commit passwords, API keys, tokens, DB/storage credentials, private certificates/keys, or populated production configuration.
- Use secret references, service identities, development-safe fixtures, and external deployment configuration.

## UI and localization

From the first user-visible implementation phase onward:

- Diwan Al Amiri branding only; preserve the owner-supplied crest unchanged.
- Navy + Gold tokens are authoritative unless approved governance changes them.
- Arabic RTL and English LTR are first-class, not post-release localization work.
- Web must remain responsive; Desktop must remain adaptive/high-DPI usable.
- Every user-facing workflow needs loading, empty, error/degraded, permission-denied, and recovery states where applicable.
- Do not expose vendor/demo/AI/mock branding in production-facing surfaces.

## Testing and security

Every behavioral change requires evidence appropriate to its risk:

- positive and negative tests for configuration/security/business invariants;
- regression coverage for fixed defects;
- clean build from the repository solution;
- repository secret scan and dependency vulnerability baseline;
- explicit failure/recovery tests for durable or destructive operations;
- responsive + RTL/LTR evidence for UI work in phases where UI is in scope.

Mocks are valid development/test tools but cannot satisfy production storage, hardware capture, deployment, or UAT acceptance gates.

## Branches, pull requests and integration

- Fetch live `main`, active claims, PRs, issues and CI before writing.
- Recover legitimate in-progress work and avoid duplicate implementations.
- Keep one lawful active phase. Future interfaces may be prepared only when required by the current phase.
- Reconcile with current `main` before merge and preserve unrelated legitimate work.
- A PR description must state scope, architecture/security impact, tests/evidence, configuration changes, and remaining external/owner-only dependencies.
- Do not merge when required automated tests have not actually executed and passed.
- Do not mark a task `CLOSED` until implementation/evidence is integrated into the intended state and no known exact-main regression remains.

## Definition of ready-to-merge

A change is ready only when all applicable items are true: implementation is complete; documentation/configuration is synchronized; tests and CI are green; security/storage invariants are preserved; no secrets are committed; migration/recovery impact is understood; user-visible states meet the active phase requirements; and the PR is reconciled with current `main`.
