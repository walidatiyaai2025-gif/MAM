# MAM Offline Demo — Windows 11

## Purpose

The Offline Demo is a separate installable MAM profile intended for demonstrations on a standalone Windows 11 x64 computer. It does not require Internet access, SQL Server, IIS, Active Directory or an external storage server.

The production architecture is unchanged: Production/UAT continue to use the centralized SQL Server deployment profile. The Demo profile is deliberately isolated and uses an embedded SQLite database plus local file-system Primary and Backup folders.

## Build output

`eng/p12-build-setups.ps1` produces the existing Desktop and Server setup files plus:

`DiwanMAM-Demo-Setup-<version>-x64.exe`

The Demo setup has a unique AppId, install directory and scheduled-task names. It can therefore be installed or uninstalled independently of the production Server/Desktop products.

The Demo artifact metadata and SHA-256 are recorded in `demo-setup-manifest.json`.

## Target system

- Windows 11 x64, build 22000 or later.
- Local administrator rights for installation.
- TCP port 80 must be available for the fixed Demo URL.
- No Internet connection is required after the installer has been obtained.
- No SQL Server or .NET runtime installation is required; API and Web are published self-contained.

## Fixed local URL

The installer adds this tagged entry to the Windows hosts file:

`127.0.0.1 demomam.da.gov.kw # Diwan MAM Demo`

The user-facing URL is always:

`http://demomam.da.gov.kw/`

The Demo Web process listens only on loopback port 80. The API listens only on `127.0.0.1:5099`; it is not exposed as a LAN service.

## Local data

Runtime data is stored outside the application install directory under:

`C:\ProgramData\Diwan Al Amiri\MAM Demo\`

Key locations:

- `database\mam-demo.db` — embedded SQLite catalog/state database.
- `primary\` — local authoritative Demo originals.
- `backup\` — separate local Demo backup copy with SHA-256 verification.
- `ingest-cache\` — local ingest staging.
- `config\appsettings.Demo.json` — materialized Demo configuration.

The SQLite provider enables foreign keys, a bounded busy timeout and WAL mode for local durability/concurrency behavior.

## Authentication

The Demo profile uses Local authentication only. The Web portal sends the built-in `admin` Demo identity to the loopback API and the API resolves it as `demo-admin` / `Administrator`. No AD/OIDC network dependency is present.

Demo seed identities also include `editor` and `viewer` for acceptance or role demonstrations.

## Runtime services

Setup registers two SYSTEM scheduled tasks that start at boot and restart on failure:

- `Diwan MAM Demo API`
- `Diwan MAM Demo Web`

The Demo does not install the production Worker scheduled task. Upload/catalog/search/metadata/category/audit and local protection state use the embedded Demo provider. Heavyweight OCR/transcription executors are not started automatically by this lightweight offline profile.

## Uninstall behavior

Uninstall stops/removes the Demo scheduled tasks and removes only the hosts-file entry tagged `# Diwan MAM Demo`.

The local Demo data directory is preserved by default so reinstall/upgrade does not destroy demo media or the SQLite catalog. `Uninstall-MamDemo.ps1 -RemoveDemoData` is available for an explicit full Demo-data purge.

## Acceptance gate

`eng/p12-demo-setup-acceptance.ps1` is executed by `.github/workflows/p12-setup-acceptance.yml`. It verifies:

- exactly one Demo Setup EXE and matching SHA-256 manifest;
- clean installation on Windows;
- embedded SQLite creation with no SQL Server connection requirement;
- fixed hosts mapping and startup tasks;
- API/Web runtime readiness;
- built-in Demo administrator authentication;
- real catalog mutation persisted across API/Web restart;
- Web-to-API loopback proxy operation;
- uninstall task/hosts cleanup with Demo database preservation.

A Demo setup is not considered accepted unless this clean-install runtime gate passes.
