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
- Physical tape playback/recording/digitization is performed outside MAM.
- MAM owns tape inventory, barcode/labels, content sheets/content indexing, digitization workflow status, upload of externally digitized files, post-ingest QC, provenance and search.
- Sony HDCAM/Betacam are tape/source-format metadata; MAM does not require deck transport control, RS-422, capture-card SDK integration or physical capture certification for Phase Two.
- Desktop and Web use the central API/business rules for tape inventory and digitized-content ingest according to permissions.
- Existing Phase One tape-capture abstractions/evidence are historical/legacy unless a future explicit ADR restores in-product capture scope.
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

The Server Setup EXE is the canonical upgrade executor. The owner must not need to paste or run a separate server-side PowerShell upgrade script for a normal future upgrade.

For an existing configured MAM Server, `DiwanMAM-Server-Setup-*.exe` must automatically:

1. detect the existing production/UAT configuration and enter protected upgrade mode;
2. skip fresh-install SQL/storage/auth/TLS wizard pages during an upgrade;
3. preserve `C:\ProgramData\Diwan Al Amiri\MAM`, Primary Storage, Backup Storage and the SQL catalog in place; never uninstall first and never delete/copy/rewrite the media library;
4. create a timestamped safety set under `C:\Temp\MAM\upgrade-safety\<timestamp>` containing current ProgramData, installed server binaries and exported API/Web/Worker task definitions;
5. read and validate the current Primary/Backup roots and require them to remain non-empty, distinct, reachable and outside the application install directory;
6. decrypt the existing SQL DPAPI secret only in memory, derive the database name without printing the connection string, create a native SQL `COPY_ONLY` + `CHECKSUM` backup to the SQL Server instance default backup directory, and run `RESTORE VERIFYONLY` before replacing application files;
7. treat the SQL backup destination as a SQL-server-side path. A drive such as `G:` returned by SQL Server must never be resolved with local MAM-host `Join-Path`/`Test-Path`;
8. stop API/Web/Worker only after safety capture and SQL backup verification succeed;
9. install the new binaries over the existing AppId/install directory;
10. merge the new configuration template with the pre-upgrade configuration so new properties are introduced while existing site values remain authoritative;
11. restore/preserve the exact pre-upgrade `sql.connection.dpapi`, `internal-auth.dpapi`, production PFX and TLS DPAPI secret so upgrades do not rotate SQL/TLS/Web↔API authentication authority;
12. apply database migrations after the verified backup and before runtime restart;
13. preserve existing custom-identity scheduled tasks when their runtime contract is current; SYSTEM tasks may be regenerated automatically;
14. refresh required firewall rules, publish the environment-bound Desktop Setup metadata, start API/Web/Worker, verify local TCP plus API/Web health endpoints, and in ActiveDirectory mode perform a signed `/api/v1/session` round-trip with the preserved `internal-auth.dpapi` key and an enabled role-bearing MAM user;
15. write pre/post-upgrade evidence and `setup-state.json`; on failure preserve the safety set and SQL backup and do not perform an automatic database/media rollback.

When `setup-manifest.json` is present beside the running Server Setup, the embedded pre-upgrade engine must verify the Server Setup SHA-256 against it automatically. The setup still records its own SHA-256 in upgrade evidence when the sidecar manifest is not present.

The normal owner workflow is therefore: **build latest verified `origin/main` -> copy the generated Server Setup (and preferably its setup-manifest sidecar) to the server -> run the Server Setup as Administrator -> let Setup perform protection, migration, restart and verification automatically.**

## Phase discipline

There is exactly one current engineering phase in `CURRENT_PHASE.md`.

Phase One P12 owner/site production-readiness evidence may remain explicitly outstanding while Phase Two engineering is active. That owner/site acceptance track is not a second engineering phase and must never be represented as PASS without real evidence.

Work may prepare future interfaces only when necessary to close the active engineering phase, but a future engineering phase must not be declared complete early.

A phase transition requires all repository/cloud-actionable implementation for the engineering phase, applicable automated acceptance, integration evidence, and exact-main regression verification.

### Owner-last production acceptance policy

Human/site/external dependencies that intrinsically cannot be produced or verified by repository/cloud execution are not allowed to stall engineering progression once every cloud-actionable requirement of the relevant engineering phase is complete. These dependencies remain `OWNER_LAST / DEFERRED_EXTERNAL` with an exact acceptance action and evidence requirement.

Typical owner-last dependencies include target-site DNS/TLS/network/firewall/NTP readiness, production SQL/storage endpoints and credentials, production identity-provider binding, signing certificates, approved preservation/retention/RPO/RTO policies, approved digitized-source ingest/preservation profiles, site printer/scanner acceptance where required, site throughput targets, target-device/browser UAT, and authorized go-live sign-off.

Deferral is never PASS. Production/go-live readiness cannot be represented as complete until every required owner-last item has real evidence.

## Definition of done for a task

A task is CLOSED only when:
- implementation/documentation is committed;
- relevant tests/evidence exist;
- no known exact-main regression is introduced;
- the implementation is integrated into the intended branch/main state;
- configuration/documentation remains synchronized;
- user-visible behavior has appropriate loading/error/permission states;
- security-sensitive behavior is enforced server-side.

A site-dependent acceptance task may instead be marked `DEFERRED_TO_OWNER / OWNER_LAST`; that status is not equivalent to CLOSED or PASS.

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
- tape digitization/ingest boundary or capture hardware/provider abstraction;
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

Repository/cloud package acceptance may be complete before target-site UAT. Target-site UAT remains owner-last and must be reported as deferred until executed.
