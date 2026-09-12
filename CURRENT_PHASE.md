# Current Phase

**Phase:** P11 — Packaging, Deployment & UAT  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Produce deployable, versioned release candidates for the Diwan Al Amiri MAM platform and prove clean installation, upgrade preservation, uninstall/data-preservation behavior, deployment validation and repository-exercisable UAT without weakening the authoritative Central API, SQL Server, Primary/Backup Storage, protection or Windows-only capture boundaries.

P11 is an engineering packaging/deployment/UAT phase. Repository/cloud evidence may close deterministic packaging, installation, migration, upgrade/uninstall, documentation, checksum and CI-exercisable UAT work. Final owner/site UAT, production signing authority/certificates, production DNS/TLS, production storage/network/identity choices and authorized go-live remain explicit P12 owner/site evidence and must not be fabricated.

## Authoritative inputs

- `PROJECT_CONTROL.md`
- `docs/OWNER_LAST_POLICY.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/ARCHITECTURE.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/phase-evidence/P10_CLOSURE.md`
- P02 Central API/SQL identity and migration boundaries
- P03/P06 authoritative Primary/Backup storage and protection invariants
- P04/P07 Worker/capture runtime and recovery boundaries
- P08 secret-safe administration/configuration
- P09 diagnostics/DR/operational support
- P10 security/performance/compatibility acceptance

## P11 required work

- [ ] Signed/versioned Desktop installer workflow; signing claims require real signing evidence.
- [ ] Server/Web/Worker deployment packages with deterministic build identity.
- [ ] SQL migration bundle and executable deployment/migration validation.
- [ ] Secret-safe production configuration template.
- [ ] Deployment validator for runtime, SQL, storage, network and configuration prerequisites.
- [ ] Upgrade/data-preservation acceptance for database/catalog/media references and durable state.
- [ ] Uninstall acceptance proving authoritative media and durable state are not silently deleted.
- [ ] Operator/admin deployment guide matching produced artifacts and configuration.
- [ ] Repeatable UAT scripts for Windows/Web, bilingual/responsive behavior and degraded/error states.
- [ ] Release notes and SHA-256 checksums for generated artifacts.
- [ ] Clean-environment installation acceptance using real generated release artifacts.
- [ ] Dedicated P11 evidence plus exact-main regression verification.

## P11 exit gate

P11 engineering closes only when:

1. clean-environment installation succeeds using generated release artifacts;
2. upgrade preserves authoritative database/catalog/media references and durable state;
3. uninstall does not delete authoritative media or silently destroy durable state;
4. installer/deployment artifacts match documented version and SHA-256 hashes;
5. SQL migration/deployment validation is executable and fails closed on invalid prerequisites;
6. operator/admin documentation matches produced artifacts and configuration;
7. repository-exercisable Windows/Web UAT, bilingual/responsive behavior and failure states are green;
8. exact-main full regression CI and dedicated P11 acceptance are green before closure.

Final owner/site UAT on agreed physical devices/browsers, production signing authority/certificates, production DNS/TLS, production storage/network/identity choices and authorized production go-live remain `DEFERRED_TO_P12 / OWNER_LAST` unless real evidence is available. Deferral is not PASS.

## Previous phase

P10 — Security, Performance & Scale Acceptance is **engineering-CLOSED**. Implementation PR #27 validated at head `53327af50a49e5210ca9aad986d3453558338bd7`; dedicated PR P10 acceptance #4 / `34691591282` SUCCESS; full PR CI #230 / `34691591280` SUCCESS; merge SHA `8e63c9385e7ab0190683f9983712ea2240eeba8f`; exact-main P10 acceptance #5 / `34691786781` SUCCESS; exact-main full CI #231 / `34691786773` SUCCESS; exact-main P09 regression #15 / `34691786782` SUCCESS. Production-scale/site/external-security evidence remains `DEFERRED_TO_P12 / OWNER_LAST` and is not PASS.

## Next phase

P12 — Production Readiness & Handover.
