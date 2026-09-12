# P11 Release Notes — 0.11.0-p11.0

Engineering release candidate for Diwan Al Amiri Media Asset Management.

## Included
- Versioned Central API, responsive Web, Worker and Windows Desktop packages.
- Executable SQL migration bundle using the repository migration runtime.
- Secret-reference-based production configuration template and deployment validator.
- Desktop install/upgrade/uninstall controls that reject application/storage overlap.
- Operator/admin deployment runbook and repository-executable bilingual UAT matrix.
- Release manifest with source commit identity and SHA-256 checksums.

## Acceptance intent
P11 acceptance proves generated artifacts can be clean-deployed in CI, that migrations execute fail-closed, that database/catalog/media references and authoritative Primary media survive an engineering upgrade, that Desktop uninstall preserves Primary/Backup roots, and that Arabic RTL / English LTR responsive/error-state regressions remain green.

## Signing status
Desktop CI artifacts are `UNSIGNED_ENGINEERING_CANDIDATE`. `Sign-MamDesktop.ps1` supports a real signing certificate and fails closed without one. No production-signature claim is made by P11 engineering evidence.

## Deferred production/site evidence
Production signing certificate/authority, final DNS/TLS/network/firewall bindings, production SQL/Primary/Backup/identity choices, target-site capacity and browser/workstation matrix, physical tape capture acceptance, final owner/operator UAT and authorized go-live are `DEFERRED_TO_P12 / OWNER_LAST`. Deferral is not PASS.
