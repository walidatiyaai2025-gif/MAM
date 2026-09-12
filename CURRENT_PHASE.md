# Current Phase

**Phase:** P08 — Enterprise Administration & Policy  
**Status:** ACTIVE  
**Repository:** `walidatiyaai2025-gif/MAM`

## Objective

Deliver authoritative, audited enterprise administration for MAM without breaking the central architecture. P08 provides server-governed policy/configuration surfaces for roles and user-policy records, metadata dictionaries/templates, capture-station policy, processing profiles, secret-safe storage/auth references, retention/delete policy, branding, notification policy, system settings, audit exploration/export, and configuration validation/test-connection behavior.

Desktop and Web administration remain Central-API-only. Clients must never receive resolved database/storage/auth credentials, filesystem roots that are not explicitly operator-safe identifiers, or direct SQL/storage adapters. All security-sensitive writes are server-authorized and auditable.

## Authoritative inputs

- `PROJECT_CONTROL.md`
- `docs/OWNER_LAST_POLICY.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/SETTINGS_REFERENCE.md`
- `docs/ARCHITECTURE.md`
- `docs/phase-evidence/P07_CLOSURE.md`
- existing P02 identity/authorization/audit boundaries
- existing P05 metadata/curation contracts
- existing P06 protection/storage contracts

## P08 required work

- [ ] Authoritative administration contracts and SQL persistence with optimistic versioning.
- [ ] Users/roles policy records without pretending that production identity-provider provisioning is locally authoritative.
- [ ] Metadata dictionary/template administration.
- [ ] Capture-station/device/profile policy administration; physical hardware certification remains P12 owner-last.
- [ ] Processing-profile administration with validation and restart-impact semantics.
- [ ] Secret-safe Primary/Backup/database/auth administrative references; resolved plaintext secrets must never be returned.
- [ ] Retention/delete policy administration with fail-closed validation.
- [ ] Branding settings administration preserving the approved Diwan Al Amiri identity constraints.
- [ ] Notification policy/destination references without committing credentials.
- [ ] General system settings with validation and explicit restart requirement.
- [ ] Audit viewer/filter/export through protected Central API.
- [ ] Validation/test-connection surface that reports safe health/result details without leaking credentials.
- [ ] Premium bilingual Windows/Web administration surfaces with loading/empty/error/degraded/permission/conflict states.
- [ ] Security boundary acceptance proving clients remain API-only and secret-safe.
- [ ] Automated P08 runtime/negative/concurrency/audit acceptance and exact-main regression CI.

## P08 exit gate

P08 engineering closes only when:

1. critical administrative settings are permission-protected and every successful/failed mutation emits audit evidence;
2. persisted records use optimistic concurrency and stale writes fail explicitly;
3. secret-backed settings store/reference opaque secret identifiers only and resolved plaintext secret values are never redisplayed;
4. invalid storage/auth/retention/profile changes fail closed before becoming effective;
5. restart-required changes declare restart impact explicitly;
6. Desktop/Web admin surfaces remain Central-API-only and preserve Arabic RTL/English LTR premium states;
7. P00–P07 regressions, P08 runtime/negative/security tests, repository secret scan and dependency scan are green on exact main.

Any production-only identity-provider binding, production credential, site endpoint, approved business policy value or authorized site acceptance is transferred to P12 as `DEFERRED_TO_P12 / OWNER_LAST` with an exact acceptance action; it is not treated as P08 PASS.

## Previous phase

P07 — Windows Tape Capture Vertical Slice is **engineering-CLOSED**. Final cloud convergence: PR #21; merge `154fc3382726fa43b06cc97f507b085f1c6384be`; PR CI #198 / `34677776281` SUCCESS; exact-main CI #199 / `34677926773` SUCCESS. Physical/site capture acceptance remains `DEFERRED_TO_P12 / OWNER_LAST` and is not PASS.

## Next phase

P09 — Reports, Monitoring, Resilience & Disaster Recovery.
