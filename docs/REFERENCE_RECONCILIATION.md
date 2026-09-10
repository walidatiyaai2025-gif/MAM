# P00 Reference Package Reconciliation

## Live repository evidence

At the P00 foundation claim on 2026-09-10, exact `main` contained governance/product/architecture/settings/branding documents, the production configuration template and branding registration, but **did not contain the historical reference package artifacts named by the phase** (`schema.sql`, historical contracts, media reference code, or the historical specification package).

Those external artifacts are therefore `DEFERRED_EXTERNAL_NOT_IN_REPOSITORY`; they are not treated as reviewed or accepted.

## Compatibility / decision matrix

| Reference area | Live source available | Current architectural disposition |
|---|---:|---|
| Historical `schema.sql` | No | Pending field-by-field mapping. Any direct client DB access, workstation-authoritative state, raw-path identity, or media BLOB-in-catalog assumption is rejected unless a later ADR explicitly changes the architecture. |
| Historical service/contracts | No | Pending method/field mapping. Adapt useful semantics behind Central API/Application contracts; never expose DB/storage credentials to clients. |
| Historical media code | No | Pending code-level mapping. Reusable inspection/transcode behavior must sit behind versioned processing/capture abstractions and may not overwrite originals in place. |
| Historical specification | No | Pending requirement trace. Product Vision, Architecture, Settings, Branding and issue #1 remain authoritative when conflict exists. |
| Local workstation media library assumption | Not required to decide | Rejected. Local media is temporary capture/recovery cache only. |
| Single storage destination / unverified copy | Not required to decide | Rejected. Primary and independently verified Backup targets are distinct. |

## Exact owner/external action

Provide or commit the historical reference package to this repository in a clearly named non-secret reference location. The next P00 reconciliation iteration must hash/inventory the supplied artifacts, map schema/contracts/behaviors to the centralized model, and close every matrix row as `ACCEPT`, `ADAPT`, or `REJECT` with rationale.

This dependency does not block the independent foundation/CI/configuration work.
