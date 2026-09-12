# P11 — Packaging, Deployment & UAT — Activation Evidence

Status: **ACTIVE**

P11 activates only after P10 engineering closure was verified on exact `main`.

## Activation prerequisite

- P10 implementation PR #27 merged at `8e63c9385e7ab0190683f9983712ea2240eeba8f`.
- Exact-main P10 acceptance #5 / `34691786781`: **SUCCESS**.
- Exact-main full CI #231 / `34691786773`: **SUCCESS**.
- Exact-main P09 regression #15 / `34691786782`: **SUCCESS**.
- Detailed closure: `docs/phase-evidence/P10_CLOSURE.md`.

## P11 objective

Produce deployable, versioned release candidates and prove installation, upgrade, uninstall/data-preservation and UAT behavior without weakening authoritative SQL/catalog/media/storage boundaries.

## Required P11 engineering work

- signed/versioned Desktop installer workflow;
- Server/Web/Worker deployment packages;
- SQL migration bundle;
- production configuration template;
- deployment validator;
- upgrade and uninstall data-preservation rules;
- operator/admin deployment guide;
- UAT scripts;
- release notes and artifact checksums;
- clean-environment installation acceptance;
- upgrade preservation acceptance for database/catalog/media references;
- uninstall acceptance proving authoritative media is not deleted;
- release artifact/version/hash reconciliation;
- repository-exercisable UAT and compatibility evidence.

## P11 exit gate

P11 engineering may close only when:

1. clean-environment installation succeeds with real generated release artifacts;
2. upgrade preserves authoritative database/catalog/media references;
3. uninstall does not delete authoritative media or silently destroy durable state;
4. installer/deployment artifacts match documented version and SHA-256 hashes;
5. SQL migration/deployment validation is executable and fail-closed;
6. operator/admin deployment documentation matches the produced artifacts;
7. repository-exercisable UAT is green;
8. exact-main CI and P11 acceptance are green before closure.

Final owner/site UAT on agreed physical devices/browsers, production signing authority/certificates, production DNS/TLS, storage/network/identity choices and authorized go-live remain P12/OWNER_LAST unless real evidence is available. Deferral is not PASS.

`UNPUSHED_WORK=NONE`
