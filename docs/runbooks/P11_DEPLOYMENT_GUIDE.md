# P11 Deployment Guide — Diwan Al Amiri MAM

## Scope
This runbook deploys P11 engineering release candidates. Production signing, production DNS/TLS, production identity/storage/network bindings, final site UAT and go-live authorization remain `DEFERRED_TO_P12 / OWNER_LAST` until real evidence exists.

## Release contents
A P11 release directory contains versioned packages for API, Web, Worker, Windows Desktop, SQL migrations and the production configuration template, plus `release-manifest.json`, `SHA256SUMS.txt` and release notes. Every package must match the manifest version and source commit.

## Required prerequisites
- .NET 10 runtime/hosting prerequisites appropriate to each component.
- SQL Server 2022 reachable by the server/service identity.
- Primary Storage and Backup Storage configured as separate, non-overlapping targets.
- Secret values supplied through approved secret references/service identities; never paste credentials into the repository template.
- Windows capture workstations only for tape capture. Web and non-Windows clients must not receive capture drivers or direct storage/database credentials.

## Deployment order
1. Verify all SHA-256 values in `SHA256SUMS.txt` and run `validate-deployment.py` against the materialized production configuration.
2. Back up SQL according to the P09 runbook before any upgrade.
3. Extract the SQL migration package and run `dotnet tool/MAM.Deployment.dll migrate <connection-string> migrations`. A migration failure is a deployment failure; do not continue.
4. Deploy the API package and confirm liveness/readiness and SQL/storage dependency status.
5. Deploy Worker, then Web, and verify they use the Central API/authoritative server configuration.
6. Install Desktop using `Install-MamDesktop.ps1`; `InstallRoot` must be separate from Primary and Backup roots.
7. Run the P11 UAT script/matrix in `docs/uat/P11_UAT.md`.

## Upgrade and rollback safety
- Authoritative SQL/catalog/media references and Primary/Backup media live outside application package directories.
- Re-running shipped migrations is required to be idempotent for the accepted schema set.
- Desktop installer performs an application-directory swap and refuses application/storage overlap.
- Never use uninstall as a media deletion mechanism. `Uninstall-MamDesktop.ps1` removes application files only and refuses unsafe root overlap.
- Database/media rollback follows the P09 backup/restore process; application rollback must use a previously verified release package and matching checksums.

## Production signing
`Sign-MamDesktop.ps1` requires a real certificate thumbprint and `signtool.exe`; it fails closed when signing authority is absent. CI P11 candidates are explicitly `UNSIGNED_ENGINEERING_CANDIDATE`. A production-signed claim is forbidden until P12 contains real certificate/signature evidence.

## Validation failures
Treat missing packages, checksum mismatch, unresolved `REPLACE-WITH-` values in a materialized configuration, non-HTTPS production URL, overlapping Primary/Backup/application roots, SQL migration failure, unavailable dependencies or failed health/UAT checks as blocking failures. Do not bypass them with a manual PASS.
