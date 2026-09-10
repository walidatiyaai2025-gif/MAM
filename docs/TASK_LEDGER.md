# Task Ledger — Diwan Al Amiri MAM

## P00 — Foundation & Reference Reconciliation

| Unit | Status | Evidence / blocker |
|---|---|---|
| P00::repository-structure | READY_FOR_CI | `MAM.sln`; Desktop/Web/API/Worker/Domain/Application/Infrastructure/checks projects on `worker/p00-foundation-baseline`. |
| P00::ci-clean-build | READY_FOR_CI | `.github/workflows/ci.yml` builds on Windows, runs foundation checks, secret baseline and dependency audit. |
| P00::build-metadata | READY_FOR_CI | `Directory.Build.props`; `BuildInfo`; `/version` endpoints. |
| P00::configuration-binding-validation | READY_FOR_CI | Typed settings loader/validator + automated mutation/fail-closed checks. |
| P00::development-config-template | READY_FOR_CI | `config/appsettings.Development.template.json`; mock targets are Development-only and non-authoritative. |
| P00::central-architecture-adr | READY_FOR_CI | ADR 0001. |
| P00::desktop-technology-adr | READY_FOR_CI | ADR 0002 selects WPF/.NET 10 baseline. |
| P00::upload-storage-contract-adr | READY_FOR_CI | ADR 0003 + application contracts. |
| P00::security-dependency-baseline | READY_FOR_CI | repo secret scanner + `dotnet list package --vulnerable`. |
| P00::reference-package-reconciliation | READY_FOR_CI | Owner-supplied reference package inventoried and mapped to ACCEPT/ADAPT/REJECT/EXTEND in `docs/REFERENCE_RECONCILIATION.md`; reusable sources preserved in `reference/mam-local-v1/`. UI source package fingerprinted as the P01 visual baseline. |
| P00::development-runbook | READY_FOR_CI | README development instructions. |

`READY_FOR_CI` is not `CLOSED`: branch CI, integration, exact-main build/test/security evidence and exact-main green CI are still required before P00 can close.
