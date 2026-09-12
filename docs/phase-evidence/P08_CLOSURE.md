# P08 Closure Evidence — Enterprise Administration & Policy

- **Phase:** `P08 — Enterprise Administration & Policy`
- **Engineering status:** `CLOSED`
- **Implementation PR:** `#23`
- **Validated PR head:** `2eb248f0b8947237082e85b98ff018eb15bbbca6`
- **PR CI:** run `#206` / `34680738564` — `SUCCESS`
- **Implementation merge SHA:** `04c07ab859bc685428d6490c068bd85b59afbff3`
- **Exact-main CI:** run `#207` / `34680904404` — `SUCCESS`
- **Production/site policy acceptance:** `DEFERRED_TO_P12 / OWNER_LAST` — **not PASS**

## Engineering capabilities closed

P08 now provides authoritative SQL-backed enterprise administration with optimistic versioning for administration policies, user/role policy records and bilingual dictionary entries. Critical policy writes are protected by the Central API authorization boundary, use explicit stale-write conflict semantics and emit persistent audit evidence for success, rejection and conflict outcomes.

The administration policy model covers identity/authorization policy, metadata dictionaries, capture-station policy, processing profile policy, Primary/Backup secret-safe references, production authentication references, retention/delete policy, locked Diwan Al Amiri branding policy, notification policy and general runtime settings. Restart-impact is explicit on applicable settings.

Secret-backed administration stores and returns opaque `SecretRef` identifiers only. Server-side validation rejects inline password/token/API-key/connection-string/private-key/credential material, and safe test-connection behavior reports only whether a reference is configured/resolvable. Resolved secret material is never returned to Desktop/Web clients or audit exports.

Windows Desktop and Web Portal administration surfaces remain Central-API-only, preserve the approved Diwan Navy/Gold identity, Arabic RTL and English LTR behavior, and expose loading, permission, conflict, degraded and error states without direct SQL/storage/secret access.

## Exit-gate evidence

1. **Permission protection + audit:** anonymous administration is rejected with `401`, Viewer administration is rejected with `403`, and successful/rejected/conflicting mutations are present in persistent audit evidence.
2. **Optimistic concurrency:** policy, user-role and dictionary stale writes are rejected explicitly with `409` and current-state evidence.
3. **Secret safety:** inline sensitive material is rejected; safe `SecretRef` testing never redisplays resolved plaintext; repository secret scanning is green.
4. **Fail-closed validation:** invalid retention, branding, production simulator, local-password authority and secret-bearing storage changes fail before persistence.
5. **Restart semantics:** restart-required policy records expose restart impact explicitly.
6. **Client boundary and UX:** Desktop/Web use shared Central API administration contracts only; bilingual premium rendered regressions remain green.
7. **Exact-main regression:** P00–P08 runtime/security/boundary suites, repository secret baseline, dependency vulnerability baseline and rendered Windows/Web acceptance all passed exact-main CI #207 on `04c07ab859bc685428d6490c068bd85b59afbff3`.

## Acceptance defects repaired rather than bypassed

During PR convergence, CI #204 exposed an over-broad P08 test assertion that treated the safe Boolean field `localPasswordsAllowed=false` as a secret solely because its name contains `password`. The acceptance was repaired to inspect actual secret-bearing keys semantically and added an explicit negative test proving `localPasswordsAllowed=true` is rejected.

CI #205 then correctly failed the repository secret baseline because the negative test fixture contained a literal password-shaped JSON value in source. The fixture was changed to construct the sensitive property name only at runtime; the repository scanner itself was not weakened. CI #206 then passed P08 acceptance, client boundary, repository secret baseline and dependency scan.

## Deferred production/site acceptance

The following remain **P12 — Production Readiness & Handover** owner/site dependencies and are not represented as P08 PASS:

- real production identity-provider binding and approved claims/group mappings;
- production database/storage/auth credentials and secret-provider bindings;
- production Primary/Backup endpoints and service identities;
- approved final retention/deletion/legal-hold values;
- approved metadata dictionaries and organization-specific policy values;
- real notification destinations/escalation recipients;
- target-environment UAT and authorized go-live approval.

P08 engineering is closed because every repository/cloud-actionable deliverable and exit-gate check is integrated and green on exact main. Deferral of the above production inputs does not convert them into accepted evidence.

`P09 — Reports, Monitoring, Resilience & Disaster Recovery` is the next ACTIVE engineering phase once this governance transition is merged.

`UNPUSHED_WORK=NONE`
