# P11 Implementation Evidence — Packaging, Deployment & UAT

**Status:** IMPLEMENTATION CANDIDATE — closure requires green PR and exact-main evidence.

## Implemented engineering scope
- deterministic versioned release packaging for API, Web, Worker, Windows Desktop, SQL migrations and production configuration;
- source commit/build identity plus SHA-256 release manifest/checksum set;
- standalone `MAM.Deployment` migration utility shipped inside the SQL bundle;
- secret-safe production configuration validation with fail-closed unresolved-placeholder and storage-root overlap checks;
- Desktop install/upgrade/uninstall scripts with application/Primary/Backup separation enforcement;
- real-certificate signing workflow that fails closed without a certificate and never fabricates a signed result;
- generated-artifact clean boot for API/Web/Worker;
- SQL clean-create/migration and idempotent upgrade execution from the generated SQL package;
- database/catalog/media-reference signature preservation and authoritative Primary object checksum preservation across upgrade;
- Desktop upgrade/uninstall preservation of external Primary/Backup markers;
- repository-executable Arabic RTL / English LTR responsive/error/degraded UAT plus rendered Windows/Web evidence;
- operator/admin deployment guide, release notes and explicit P12 owner/site deferrals;
- dedicated `p11-acceptance` GitHub Actions workflow.

## Safety boundaries retained
- clients remain Central-API-only;
- permanent media remains server-side Primary plus separately verified Backup;
- application install/uninstall paths may not overlap authoritative storage roots;
- production secrets remain references, not committed values;
- production signing is not claimed without real certificate evidence;
- site-specific UAT, hardware, production identity/network/storage bindings and go-live remain P12.

## Closure evidence still required
P11 may be marked engineering-CLOSED only after implementation PR P11 acceptance, full regression CI and retained P09/P10 workflows succeed, the implementation is merged, and the same gates succeed on exact `main`. Until then this document is implementation evidence only, not phase closure.
