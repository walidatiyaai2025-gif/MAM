# P12 Offline Demo Evidence

Status: IMPLEMENTATION CANDIDATE — acceptance is authoritative only after the exact-head pull-request gates pass.

## Contract

- Separate installer product: `DiwanMAM-Demo-Setup-<version>-x64.exe`.
- Windows 11 x64 only (build 22000+).
- Fixed local URL: `http://demomam.da.gov.kw/`.
- Installer-managed hosts mapping to `127.0.0.1`.
- Embedded SQLite database; no SQL Server dependency.
- Self-contained API/Web payload; no separately installed .NET runtime required.
- Local file-system Primary and Backup roots under the Demo ProgramData directory.
- Built-in local Demo administrator identity; no AD/OIDC dependency.
- Demo product has unique AppId, install root and task names and does not alter Production/UAT database-provider rules.

## Required exact-head gate

`.github/workflows/p12-setup-acceptance.yml` must build all three setup artifacts and run both:

1. existing Desktop/Server production setup acceptance; and
2. `eng/p12-demo-setup-acceptance.ps1` clean-install runtime acceptance.

The Demo acceptance verifies installer identity/hash, runtime configuration, SQLite creation, hosts mapping, scheduled tasks, API/Web readiness, local Administrator session, persisted catalog mutation across restart, Web proxying and uninstall cleanup/preservation behavior.

No PASS is recorded in this file until the exact pull-request head is green.
