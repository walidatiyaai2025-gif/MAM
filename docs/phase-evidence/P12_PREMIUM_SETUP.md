# P12 Premium Dual Setup

**Phase:** P12 — Production Readiness & Handover  
**Scope:** repository-controlled packaging and installer automation for Windows Desktop and Server/Web stack  
**Status:** IMPLEMENTED — PR/exact-main acceptance pending  
**Base main:** `efed807ad517589f7857d01a913b5812dff21a20`

## Owner requirement

Deliver two premium Diwan Al Amiri branded Setup EXEs with a Next → Next workflow and no post-install manual editing of application configuration, environment variables, Windows startup tasks or firewall rules.

## Desktop Setup

`DiwanMAM-Desktop-Setup-<version>-x64.exe`

The wizard owns:

- installation path and shortcuts;
- Central API base URL;
- capture cache path;
- optional certified capture-provider identifier;
- upgrade behavior (preserve existing managed client configuration by default).

The Desktop reads `%ProgramData%\Diwan Al Amiri\MAM\desktop.setup.json` automatically during application startup. No manual environment-variable editing is required.

## Server/Web Setup

`DiwanMAM-Server-Setup-<version>-x64.exe`

The wizard owns:

- Production vs UAT deployment mode;
- public DNS host, API port and Web port;
- SQL Server connection string;
- Primary and Backup storage roots;
- database backup policy identifier;
- authentication-mode configuration value;
- recycle/audit retention values and audit-read policy label;
- Local System vs custom Windows/domain service identity;
- Production PFX certificate + password;
- database creation/migrations, firewall rules and component startup options.

Setup installs self-contained win-x64 API, Web, Worker and deployment tooling. It creates managed Windows startup tasks for API/Web/Worker, creates firewall rules when selected, generates the server configuration, and can create/migrate the SQL database without requiring manual SQL scripts.

## Secret handling

The SQL connection string is never written to `appsettings`. Setup writes only `env:MAM_SQL_CONNECTION_STRING` as the secret reference. The actual SQL secret is protected using Windows DPAPI LocalMachine and stored under the restricted ProgramData MAM secret directory. Production PFX password is protected the same way. Temporary plaintext inputs are deleted after configuration. A custom service-account password is used only for Windows task registration and is not written to MAM configuration.

## Branding

Both Setup EXEs derive their installer visual assets from the same approved owner-supplied Diwan Al Amiri crest bytes already embedded in `DiwanCrestData`. The build fails if the canonical crest fingerprint differs from:

`bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`

The Setup identity uses Diwan Al Amiri naming and Navy/Gold presentation. The source crest is not recolored or replaced.

## Upgrade/uninstall behavior

- Desktop upgrades preserve existing setup-managed client configuration unless replacement is explicitly chosen in the wizard.
- Server uninstall removes MAM startup tasks and installer-managed firewall rules.
- Server configuration, encrypted secrets and data are preserved by default so uninstall/reinstall does not destroy operational state.
- Setup code-signing remains an OWNER_LAST production requirement and is not falsely represented as complete without the authorized certificate/key.

## Acceptance gate

`.github/workflows/p12-setup-acceptance.yml` must prove on Windows:

1. both Setup EXEs compile;
2. executable metadata is Diwan Al Amiri branded;
3. Desktop silent install writes managed configuration and uninstall preserves it;
4. Server silent UAT install creates all runtime components, managed configuration, DPAPI SQL secret and startup tasks without exposing plaintext SQL credentials in config;
5. Server uninstall removes startup tasks but preserves configuration/data;
6. exact setup artifact SHA-256 values are recorded;
7. the approved crest fingerprint is preserved.

## P12 boundary

This work closes repository-controlled installer automation only after PR and exact-main gates are green. It does **not** mark DNS/TLS approval, production service identity, production SQL/storage topology, hardware certification, identity-provider integration, code-signing custody, final site UAT or go-live authorization as PASS.

`UNPUSHED_WORK=NONE`
