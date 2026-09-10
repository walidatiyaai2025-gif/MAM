# Task Ledger — Diwan Al Amiri MAM

## P00 — Foundation & Reference Reconciliation

| Unit | Status | Evidence / blocker |
|---|---|---|
| P00::repository-structure | READY_FOR_CI | `MAM.sln`; Desktop/Web/API/Worker/Domain/Application/Infrastructure/checks projects on `worker/p00-foundation-baseline`. |
| P00::ci-clean-build | READY_FOR_CI | `.github/workflows/ci.yml` performs clean restore/build, foundation checks, secret baseline and dependency vulnerability audit. P00 cross-targets WPF with `EnableWindowsTargeting=true`; native Windows runtime acceptance remains a later gate. Latest GitHub-hosted job has not received a runner (`runner_id=null`), so no build result is fabricated. |
| P00::build-metadata | READY_FOR_CI | `Directory.Build.props`; `BuildInfo`; `/version` endpoints. |
| P00::configuration-binding-validation | READY_FOR_CI | Typed binding covers the documented foundation-critical Server/DB/Storage/Desktop cache/Upload/Capture/Jobs/Search/Auth/Retention/Audit/Logging/Diagnostics/Brand sections. Unknown JSON keys are rejected; automated negative checks cover missing/unsafe storage, protection, logging, job lease and schema cases. |
| P00::development-config-template | READY_FOR_CI | `config/appsettings.Development.template.json` contains secret-free development values for the typed contract; mock storage is Development-only and non-authoritative. Production template remains deliberately invalid until deployment/site placeholders are resolved. |
| P00::central-architecture-adr | READY_FOR_CI | ADR 0001. |
| P00::desktop-technology-adr | READY_FOR_CI | ADR 0002 selects WPF/.NET 10 baseline. |
| P00::upload-storage-contract-adr | READY_FOR_CI | ADR 0003 + application contracts. |
| P00::security-dependency-baseline | READY_FOR_CI | `eng/verify-repo.ps1` scans tracked source/config/reference text including imported `.sql/.ts/.py/.md/.txt`; CI also runs the dependency vulnerability baseline. |
| P00::reference-package-reconciliation | READY_FOR_CI | Owner-supplied reference package inventoried and mapped to ACCEPT/ADAPT/REJECT/EXTEND in `docs/REFERENCE_RECONCILIATION.md`; reusable sources preserved in `reference/mam-local-v1/`. UI source package fingerprinted as the P01 visual baseline. |
| P00::development-runbook | READY_FOR_CI | README development instructions include the strict config boundary and P00 cross-target CI rationale. |

## Current integration blocker

`READY_FOR_CI` is not `CLOSED`. GitHub Actions run allocation has not begun executing repository code. Previous `windows-latest` and subsequent `ubuntu-latest` attempts both exposed `runner_id=null` with zero executed steps; switching runner families therefore did not reveal a repository/test failure and confirmed the dependency is hosted-runner allocation. The authoritative next chain remains: successful latest-head PR CI -> merge PR #2 -> successful exact-main CI -> P00 closure evidence. No unit is marked `CLOSED` before those results exist.
