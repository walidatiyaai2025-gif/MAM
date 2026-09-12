# P10 Threat Model — Diwan Al Amiri MAM

## Status and scope

This document is the engineering threat-model review for `P10 — Security, Performance & Scale Acceptance`.
It covers the implemented P00–P09 product boundaries on repository-controlled and CI-exercisable infrastructure. It is not an external penetration-test report and it does not certify production/site capacity.

## Security invariants

1. Windows Desktop and Web clients use the Central API for authoritative catalog/media/administration/operations workflows.
2. Client applications do not receive SQL credentials, Primary Storage roots, Backup Storage roots, worker credentials, or resolved secret values.
3. Permanent originals are server-managed. Windows capture cache/staging is temporary and is not authoritative storage.
4. An upload is not promoted to Primary Storage until server-side length and SHA-256 verification succeeds.
5. A media asset is not `Protected` until the required independent Backup copy and checksum verification succeeds.
6. Mutating catalog/curation/upload/processing endpoints require write permission; administrative/protection/operations endpoints require administrative permission; audit surfaces require audit-read permission.
7. Missing authoritative SQL/secret dependencies fail closed outside development rather than silently falling back to a writable local authority.
8. Tape capture remains a Windows-only professional-hardware capability. Web never becomes a hardware-capture authority.
9. Arabic RTL and English LTR are presentation modes over the same authorization and Central API boundaries.

## Trust zones and data flows

| Zone | Trusted responsibility | Untrusted / hostile input to assume |
|---|---|---|
| Windows Desktop | operator UX, temporary capture/upload staging, Central API client | filenames, selected files, tape/device data, local user actions, network failures |
| Web Portal | responsive operator/admin UX, Central API proxy/client | browser input, route/query/form values, stale sessions, network failures |
| Central API | authentication/authorization, validation, orchestration, safe response shaping | all client headers, IDs, JSON, query strings, upload byte streams |
| SQL Server | authoritative metadata, sessions, jobs, policy, audit and operational state | application queries/parameters only; direct client access is forbidden |
| Primary Storage | authoritative original + derivative object storage | server-generated object keys and verified server writes only |
| Backup Storage | separate verified protection copy | server-managed protection jobs only |
| Worker | durable processing/protection leases and retries | queued media, tool failures, crashes, dependency degradation |
| Secret resolver | server-side resolution of secret references | missing/misbound secret references; plaintext must not be logged or returned |
| Capture hardware | physical tape/capture signal on approved Windows station | physical/device/driver instability; final hardware acceptance is P12 |

## Threat register and mitigations

| Threat | Primary attack/failure path | Implemented mitigation / P10 evidence | Residual disposition |
|---|---|---|---|
| Anonymous API access | Call protected API without identity | ASP.NET authorization policies; P10 negative suite verifies representative catalog/upload/admin/operations routes return 401 | Engineering controlled |
| Privilege escalation | Viewer/editor invokes write/admin endpoints | Permission claims + named policies; P10 negative suite verifies 403 on representative mutations/admin/operations | Engineering controlled; production IdP mapping is P12 |
| IDOR / unsafe object reference | Guess asset/session/collection/job identifiers | authorization occurs before object lookup; GUID routes; not-found semantics; P10 random-object negative checks | Product has role-based, not tenant/owner-scoped, authorization. If future per-owner ACLs are introduced, a new ownership policy is required |
| Path traversal | filename such as `../x`, rooted path or separator injection | server normalizes filename and forbids path components/invalid filename characters; server generates storage object keys | Engineering controlled |
| Oversized upload / memory pressure | forged declared size or chunk bigger than policy | configured maximum file size, bounded chunk size, streaming request read with hard chunk limit | Site storage/network capacity is P12 |
| Hash substitution / corrupt promotion | incorrect per-chunk or final digest | SHA-256 syntax validation, per-chunk digest verification, final server-side length/SHA-256 verification, post-write Primary verification | Engineering controlled |
| Duplicate race | duplicate content submitted concurrently | SHA-256 duplicate lookup before session and again before final promotion; authoritative DB/object promotion checks | P10 concurrent-ingest acceptance re-verifies no corrupt/duplicate identity in representative parallel ingest |
| Extension/MIME spoofing | unsafe bytes named with allowed extension | upload allow-list is not treated as content proof; unknown extension is quarantined; P04 inspection/proxy execution is the content-validation boundary and fails processing rather than blessing invalid media | Malware/AV/CDR product integration and external security policy are P12/site decisions unless separately procured |
| Quarantine bypass | unknown file promoted to Primary | quarantine flag is durable and finalize rejects promotion | Engineering controlled |
| SQL injection | hostile search/admin strings | parameterized `SqlCommand` usage in authoritative SQL services; P10 hostile query inputs remain bounded and return normalized responses | Engineering controlled within implemented endpoints |
| Secret disclosure | exception/diagnostics/log response exposes credentials | environment secret resolver is server-only; diagnostics intentionally omit values/roots; repo secret scan is hard gate | Production secret provisioning/rotation is P12 |
| Direct client SQL/storage access | client bypasses API | source/acceptance boundary checks forbid SQL/storage dependencies in Desktop/Web; storage keys/roots remain server-side | Engineering controlled |
| Worker double processing | concurrent workers lease same durable job | SQL-backed leases/state transitions, deterministic derivative/job identity, retry/stale recovery acceptance | Target-site worker-count/capacity is P12 |
| Worker crash / lost work | process dies after lease | durable job state, lease expiry, retry and stale recovery; P04/P06/P09 acceptance re-run by exact-main CI/P10 convergence | Engineering controlled |
| Storage/protection false success | Backup/Primary unavailable | health/degraded states and fail-closed promotion/protection semantics; no `Protected` before checksum verification | Site HA/capacity is P12 |
| Correlation/log injection | attacker supplies malformed correlation value | correlation ID character/length allow-list; otherwise server generates value | Engineering controlled |
| XSS via metadata/title | stored/user text rendered into web UI | Web shell escapes dynamic text before HTML insertion for central catalog rows/state labels; request payloads remain data, not markup | Browser security headers/CSP deployment policy should be finalized with P11/P12 hosting profile |
| CSRF/session deployment mismatch | browser credential mode differs in production | current development identity is header-based and not a production browser auth mechanism | Production identity/session/cookie/anti-forgery design is P12 binding work and cannot be claimed PASS in development |
| RTL/LTR security drift | alternate locale hides or bypasses controls | language switch changes `lang`/`dir` presentation only; routes/API policies remain identical; P10 UI regression covers both directions | Engineering controlled |
| Denial of service / saturation | high API/search/ingest/worker pressure | bounded page/bulk/chunk sizes, durable queues, explicit dependency health, retry/degraded behavior; P10 records representative hosted-runner baselines | Production SLA/capacity/load targets are P12 OWNER_LAST |

## P10 engineering acceptance strategy

P10 closes engineering evidence only when all of the following are green on the implementation PR and then on exact `main`:

- dedicated P10 security/scale workflow;
- representative authorization and object-reference negatives;
- upload/path/size/hash/quarantine/content-inspection negatives;
- measured hosted-runner API, SQL-backed search and concurrent-ingest baselines;
- P04/P06 durable worker/storage/protection failure/recovery regression;
- repository secret scan and transitive dependency vulnerability scan;
- Windows build/rendered visual acceptance and Web render smoke evidence;
- accessibility/RTL/LTR/responsive source/rendered checks;
- full repository CI plus dedicated P09 resilience/DR workflow.

Performance numbers generated by CI are regression baselines only. They are intentionally not production SLAs.

## Deferred / non-PASS residual risks

The following require real P12 owner/site evidence and remain `DEFERRED_TO_P12 / OWNER_LAST`:

- approved production SLA, throughput, concurrency and capacity targets;
- target-site sustained load/capacity certification for API, SQL, network, Primary and Backup storage;
- final physical workstation/browser/device matrix;
- real tape deck/capture-card/driver acceptance;
- production identity-provider binding, group/claims mapping, MFA/session policy and secret rotation;
- external penetration testing or security authority sign-off where required;
- production WAF/reverse-proxy/TLS/security-header policy and network segmentation validation;
- production malware scanning/CDR controls if mandated by site security policy;
- final operational/security/go-live authorization.

Deferral is not PASS.