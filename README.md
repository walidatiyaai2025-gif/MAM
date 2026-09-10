# Diwan Al Amiri MAM

Centralized **Media Asset Management (MAM)** platform for **Diwan Al Amiri**.

> Status: P00 foundation / architecture baseline

## Product goal

Build a premium, enterprise-grade media archive used from multiple Windows workstations and web browsers. End users can capture from professional tape hardware on Windows or upload existing video/image/audio/PDF files from Windows/Web. Authoritative media is stored on central server-side storage, never as a permanent workstation library.

## Target deployables

- `MAM.Desktop` — Windows capture/upload/operations client (WPF, .NET 10 baseline).
- `MAM.Web` — responsive browser portal consuming the Central API.
- `MAM.Api` — authoritative business/security boundary and owner of server configuration.
- `MAM.Worker` — durable processing/backup worker host with server-side configuration.
- SQL Server — authoritative catalog/state store in later phases.
- Primary Storage + independently configured and checksum-verified Backup Storage.

`MAM.Desktop` and `MAM.Web` reference the shared Application layer, not server-side Infrastructure. They do not load SQL Server or permanent-storage configuration. The Central API/Worker boundary owns those settings and credentials.

## Repository structure

```text
src/
  MAM.Domain/
  MAM.Application/
  MAM.Infrastructure/
  MAM.Api/
  MAM.Web/
  MAM.Worker/
  MAM.Desktop/
tests/
  MAM.Foundation.Checks/
config/
docs/
  adr/
eng/
```

## Development baseline

Prerequisite: .NET 10 SDK. Windows is required to **run** the WPF Desktop client. P00 CI cross-targets the Windows project from a clean `ubuntu-latest` runner with `EnableWindowsTargeting=true` so foundation build/config/security acceptance is not dependent on Windows-hosted runner availability. Native Windows runtime, capture-device and packaging acceptance remain mandatory in their later phase gates.

```powershell
dotnet restore MAM.sln
dotnet build MAM.sln -c Release
dotnet run --project tests/MAM.Foundation.Checks/MAM.Foundation.Checks.csproj -c Release -- config/appsettings.Development.template.json config/appsettings.Production.template.json
./eng/verify-repo.ps1
```

Run the API using the secret-free Development template copied into its output:

```powershell
dotnet run --project src/MAM.Api/MAM.Api.csproj
```

Run the Web foundation surface (client-safe; it does not load the server configuration template):

```powershell
dotnet run --project src/MAM.Web/MAM.Web.csproj
```

Run Desktop on Windows:

```powershell
dotnet run --project src/MAM.Desktop/MAM.Desktop.csproj
```

For **server-side API/Worker deployments**, set `MAM_CONFIG_PATH` to an external site-specific JSON file. Never commit the populated production file or secrets. The checked-in production template is deliberately invalid until required placeholders/site values are supplied. Configuration loading rejects undocumented keys instead of silently ignoring them.

## Build identity

Build metadata is stamped into the shared Application assembly and surfaced by the deployables: API `/version`, Web `/version` and foundation page, Worker startup output, and the Desktop foundation footer. CI verifies that commit SHA, build number and UTC build timestamp are not left at local placeholder values.

## Core storage/protection invariant

An asset is not `Protected` until the Primary original and Backup original are independently readable and their SHA-256 values match. Workstation capture cache is temporary recovery protection only.

## Branding and UX

The product is exclusively branded for Diwan Al Amiri. The owner-supplied crest remains unmodified; application chrome uses the locked Navy + Gold design tokens. Arabic RTL and English LTR are first-class. P01 owns the complete premium responsive shell rather than treating the P00 foundation surface as final UI.

## Authoritative documentation

- `docs/PRODUCT_VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/BRANDING_UI_UX.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/REFERENCE_RECONCILIATION.md`
- `docs/TASK_LEDGER.md`
- `PROJECT_CONTROL.md`
- `CURRENT_PHASE.md`

## Non-negotiables

- No permanent local-only media library.
- No direct database access from Desktop/Web.
- No server Infrastructure/configuration dependency from Desktop/Web.
- No silent write failover from Primary to Backup.
- No `Protected` state before checksum parity.
- Tape capture is Windows-only; file upload is Windows + Web where policy permits.
- No secrets/production credentials in source control.
- Premium responsive RTL/LTR quality is required from the first user-visible phase.
