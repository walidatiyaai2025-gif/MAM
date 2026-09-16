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

## Canonical owner workstation paths

Unless the owner explicitly overrides them for a specific run, all future Windows PowerShell update/build instructions must use these fixed paths:

- Local repository/worktree: `C:\Live\MAM`
- Setup output: `C:\Live\MAM\artifacts\setups`

`C:\Live\MAM` is only the local working copy. It is **not** the update source. The authoritative update source is always the latest remote `origin/main` from `walidatiyaai2025-gif/MAM`.

Use the existing local repository in place; do not create a second clone or invent an alternate build root for normal owner workflows. Every update/build run must contact the remote repository first and fetch the latest `origin/main` before reading the product version, selecting code to build, or creating setup artifacts. Never build from a merely existing/stale local `main` checkout.

The authoritative installer builder is `eng\p12-build-setups.ps1`. Normal owner builds must emit the Desktop Setup, Server Setup, `setup-manifest.json`, and `brand-manifest.json` under `C:\Live\MAM\artifacts\setups`.

Before any server-side installation command is supplied, identify the exact generated `DiwanMAM-Server-Setup-*.exe` from that setup directory, record its SHA-256, copy that exact artifact to the server, and then run the server setup. Do not assume a server-side `$Setup` path before the artifact has actually been built and copied.

### Canonical owner PowerShell update/build workflow

When the owner asks for a PowerShell script to update/build MAM, use one stable end-to-end plan. Do not rotate between alternate paths, partial snippets, or different build strategies unless the owner explicitly requests a different workflow or the repository itself changes the authoritative builder.

The standard workflow is:

1. Run one complete PowerShell block as Administrator with `$ErrorActionPreference = 'Stop'` and a top-level `try/catch` so a failed step cannot fall through into false success output.
2. Use only `C:\Live\MAM` as the local worktree and `C:\Live\MAM\artifacts\setups` as setup output.
3. Verify the existing repository and protect tracked local changes. If tracked changes exist, stop and report them; never destroy them automatically.
4. **Fetch remote first.** Run `git fetch origin --prune` before trusting any local branch, version, SHA, or build input. The desired build source is the newest fetched `origin/main`, not the pre-existing local checkout.
5. After fetch succeeds, checkout local `main` and hard-reset it to the newly fetched `origin/main` only after the tracked-change safety check passes.
6. Verify synchronization explicitly before build: local `HEAD` must equal the fetched `origin/main` SHA. If they differ, stop; never continue with a stale or divergent local source tree.
7. Only after the remote synchronization check passes, read `Directory.Build.props`, product version, source SHA, migrations, and all other build inputs from the synchronized worktree.
8. Verify .NET 10 SDK before build.
9. Resolve Inno Setup 6 before invoking the official builder. Search the known locations below, then PATH, then install with WinGet if required, then search again:
   - `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
   - `C:\Program Files\Inno Setup 6\ISCC.exe`
   - `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`
   - fallback recursive search limited to the corresponding Program Files / LocalAppData program roots.
10. Once the real `ISCC.exe` is found, add its directory to the current PowerShell process PATH. If the official builder's `${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe` lookup would still miss the real compiler, adjust `${env:ProgramFiles(x86)}` for the current process only so the authoritative builder resolves the discovered Inno installation. Verify both `Test-Path` on the builder-resolved compiler path and `Get-Command ISCC.exe` before starting the setup build.
11. Set `GITHUB_SHA` to the verified fetched `origin/main` SHA and a local `GITHUB_RUN_NUMBER`, and clean only the canonical setup output artifacts.
12. Invoke only `C:\Live\MAM\eng\p12-build-setups.ps1 -OutputRoot C:\Live\MAM\artifacts\setups` for the installer build. Do not substitute a hand-built publish/package pipeline for normal owner update builds.
13. Treat the build as successful only after the output directory contains exactly two installer EXEs, including one `DiwanMAM-Desktop-Setup-*` and one `DiwanMAM-Server-Setup-*`, and the expected manifests are generated by the official builder.
14. Calculate SHA-256 for both installers and print `MAM BUILD SUCCESS` only after the real files and hashes exist. If any earlier step fails, print/report `BUILD FAILED` and stop; do not continue into hash commands, empty variables, server setup instructions, or a misleading success banner.
15. For the next server-update step, carry forward the exact generated Server Setup filename and SHA-256 from this successful local build. Never guess the server installer name/path from a previous build.

The invariant for every owner update request is: **remote `origin/main` is fetched and verified first; the local path is only where that latest remote state is materialized and built.**

This workflow is the default owner update/build contract for future requests. A future response should provide this single plan directly instead of repeating the earlier trial-and-error Inno Setup discovery process.

### Canonical owner server upgrade workflow

When the owner asks for the server PowerShell update/upgrade script, use one stable fail-closed workflow designed to protect stored data before any installer is launched.

The server-side contract is:

1. Run one complete PowerShell block as Administrator with `$ErrorActionPreference = 'Stop'` and a top-level `try/catch`.
2. The canonical server setup drop folder is `C:\Temp\MAM`.
3. Never guess the installer from modification time alone when a manifest is available. Read `C:\Temp\MAM\setup-manifest.json`, select the exact `DiwanMAM-Server-Setup-*.exe` listed in that manifest, require that exact file to exist, and require its SHA-256 to match the manifest before proceeding.
4. Never uninstall the existing MAM Server before an upgrade. Never delete `C:\ProgramData\Diwan Al Amiri\MAM`, Primary Storage, Backup Storage, the SQL database, or any preserved media as part of a normal update.
5. Before launching Setup, capture a timestamped safety set under `C:\Temp\MAM\upgrade-safety\<timestamp>` containing at minimum the current MAM ProgramData tree, current installed MAM Server binaries when present, setup/config state, and exported scheduled-task definitions for API/Web/Worker.
6. Read the current production configuration before upgrade and record the authoritative Primary Storage root and Backup Storage root. Both must be non-empty, distinct, reachable, and outside the application install directory. The upgrade script must not copy, move, rename, truncate, clean, or rewrite stored media in either root.
7. Protect the SQL catalog before migrations. Decrypt the existing `sql.connection.dpapi` secret only in memory using Windows DPAPI LocalMachine, derive the configured database name without printing the connection string, create a native SQL `COPY_ONLY` backup with `CHECKSUM` to the SQL Server instance default backup directory, and run `RESTORE VERIFYONLY` against that backup. If the SQL backup or verification cannot complete, stop before launching Setup. Never expose or persist the plaintext SQL connection string in logs or command-line arguments.
8. Record the pre-upgrade storage roots, database name, verified SQL backup location, installer filename/hash, setup state, and safety-set path in a pre-upgrade evidence file. Do not compute full-media hashes or recursively duplicate the media library during a routine upgrade; Primary/Backup Storage are preserved in place and are outside installer ownership.
9. Launch the verified Server Setup interactively over the existing installation. Keep database creation/migrations enabled unless a specific approved recovery procedure says otherwise. Use the existing production values; never switch storage roots or identity/TLS settings casually during an update.
10. After Setup returns success, require `setup-state.json`, the API/Web/Worker scheduled tasks, and configured listening endpoints to exist. Re-read the production configuration and require Primary Storage and Backup Storage roots to remain exactly the same as before the upgrade. A storage-root change is a hard failure requiring investigation.
11. If setup/configuration fails, keep the SQL backup and timestamped safety set intact and report the failure. Do not automatically restore the database or overwrite production data; rollback/restoration is a separate explicit recovery action.
12. Print `MAM SERVER UPDATE SUCCESS` only after installer verification, pre-upgrade safety capture, verified SQL backup, successful Setup exit, preserved storage roots, and post-upgrade component checks all pass.

The invariant for every owner server update request is: **verify exact artifact -> protect configuration and database -> preserve Primary/Backup media in place -> upgrade over the existing installation -> verify the same storage roots and runtime after upgrade.**

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
