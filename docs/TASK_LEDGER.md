# Task Ledger — Diwan Al Amiri MAM

## P00 — Foundation & Reference Reconciliation

| Unit | Status | Evidence / blocker |
|---|---|---|
| P00::repository-structure | READY_FOR_CI | `MAM.sln`; Desktop/Web/API/Worker/Domain/Application/Infrastructure/checks projects on `worker/p00-foundation-baseline`. Client dependency direction is guarded: Web/Desktop reference Application, while server-side API/Worker may reference Infrastructure. |
| P00::engineering-contribution-standards | READY_FOR_CI | `CONTRIBUTING.md` + `AGENTS.md` define project boundaries, coding/testing/security/configuration/UI rules, live-state recovery, PR integration and evidence requirements. |
| P00::ci-clean-build | READY_FOR_CI | `.github/workflows/ci.yml` performs clean restore/build, foundation checks, repository secret baseline and dependency vulnerability audit. P00 cross-targets WPF with `EnableWindowsTargeting=true`; native Windows runtime acceptance remains a later gate. GitHub-hosted jobs have repeatedly remained queued without an assigned runner/steps, so no build result is fabricated. |
| P00::build-metadata | READY_FOR_CI | `Directory.Build.props` stamps commit SHA/build number/UTC timestamp into the shared `MAM.Application.Diagnostics.BuildInfo`. Build identity is surfaced by API `/version`, Web `/version` + foundation page, Worker startup output and Desktop footer. Foundation checks reject CI builds left at local placeholder identity. |
| P00::configuration-binding-validation | READY_FOR_CI | Typed server-side binding covers the documented foundation-critical Server/DB/Storage/Desktop cache/Upload/Capture/Jobs/Search/Auth/Retention/Audit/Logging/Diagnostics/Brand sections. Unknown JSON keys and null critical sections fail closed. Semantic validation rejects unsupported environment/auth modes, non-Explicit migration mode, insecure Production origins, unsafe API base paths, Production mock storage, rooted/traversal managed storage paths, path-like upload extension values, missing/unsafe storage, broken protection, sensitive logging and invalid durable-job lease/heartbeat state. Automated negative checks cover these cases. |
| P00::development-config-template | READY_FOR_CI | `config/appsettings.Development.template.json` contains secret-free development values for the server-side typed contract; mock storage is Development-only and non-authoritative. Production template remains deliberately invalid until deployment/site placeholders are resolved. Web/Desktop do not load this server configuration. |
| P00::central-architecture-adr | READY_FOR_CI | ADR 0001 fixes Central API, SQL Server catalog, server-side Primary/Backup and temporary workstation cache boundaries. `MAM.Foundation.Checks` explicitly rejects Infrastructure references from Web/Desktop and Web-side `MamSettingsLoader` use. |
| P00::desktop-technology-adr | READY_FOR_CI | ADR 0002 selects WPF/.NET 10 baseline. |
| P00::upload-storage-contract-adr | READY_FOR_CI | ADR 0003 + application upload/storage contracts. |
| P00::capture-boundary-adr | READY_FOR_CI | ADR 0004 + `MAM.Application.Capture.ICaptureProvider` establish a vendor-neutral Windows hardware boundary and temporary-cache -> Central API durable upload -> Primary -> separately verified Backup flow. No hardware/mock acceptance is claimed. |
| P00::security-dependency-baseline | READY_FOR_CI | `eng/verify-repo.ps1` scans tracked source/config/reference text including imported `.sql/.ts/.py/.md/.txt`; CI also runs the dependency vulnerability baseline. |
| P00::reference-package-reconciliation | READY_FOR_CI | Owner-supplied reference package inventoried and mapped to ACCEPT/ADAPT/REJECT/EXTEND in `docs/REFERENCE_RECONCILIATION.md`; reusable sources preserved in `reference/mam-local-v1/`. UI source package fingerprinted as the P01 visual baseline. |
| P00::development-runbook | READY_FOR_CI | README development instructions document the server/client configuration boundary, build identity surfaces and P00 cross-target CI rationale. |

## Current integration blocker

`READY_FOR_CI` is not `CLOSED`. The P00 implementation and documentation are integrated on the single lawful PR branch, but required GitHub Actions execution has not produced a successful latest-head result. Repeated `windows-latest` and `ubuntu-latest` jobs have remained queued before repository steps with no assigned hosted runner. This external execution state is neither PASS nor a repository test failure.

The authoritative closure chain remains: successful latest-head PR CI -> merge PR #2 -> successful exact-main CI -> P00 closure evidence. No unit is marked `CLOSED` before those results exist.
