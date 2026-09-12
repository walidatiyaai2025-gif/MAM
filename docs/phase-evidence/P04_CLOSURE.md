# P04 — Media Inspection, Proxies & Previews — Closure Evidence

Status: **CLOSED**

P04 closed only after the implementation PR was green, merged, and the full phase-exit workflow succeeded again on exact `main`.

## Verified integration identity

- Implementation PR: **#13 — P04: media inspection, proxies, previews and durable processing**
- Validated PR head: `f185a651cbe7fe3dea1b9f78603e56ec5ff7a1e8`
- PR CI: run **#150** / `34664627802` — **SUCCESS**
- Implementation merge SHA: `c1b432061a936b34192a853ce2b863e7e96352be`
- Exact-main phase-exit CI: run **#151** / `34664757705` — **SUCCESS**

## Exit-gate mapping

1. **Representative uploaded asset is inspected and authoritative technical metadata is persisted/retrievable — PASS.**
   - `SqlServerMediaProcessingService` uses FFprobe through the worker boundary and persists `MamTechnicalMetadata` in SQL Server.
   - P04 acceptance retrieves video technical metadata through the Central API after durable processing.

2. **Primary original remains byte-for-byte untouched — PASS.**
   - Processing verifies the Primary original SHA-256/length before processing and verifies it again after derivative generation.
   - Acceptance compares video, image, audio and PDF Primary SHA-256 values before/after processing.

3. **Versioned deterministic derivatives — PASS.**
   - Built-in versioned profiles cover inspection, video proxy, image preview, audio preview and PDF inline strategy.
   - Derivative identity/object key is deterministic from asset identity, profile/version and original SHA-256.
   - Re-enqueue of the same asset/profile returns the same durable job/derivative identity rather than duplicate work.

4. **Failed/interrupted jobs retry or recover without silent loss/corruption — PASS.**
   - Acceptance intentionally crashes a worker immediately after lease acquisition with exit code 86.
   - After lease expiry a second worker reclaims the same durable job, increments the attempt count and completes it.
   - Invalid media/profile processing records an explicit Failed state and supports explicit retry.

5. **Processing state survives worker/API restart — PASS.**
   - SQL-backed job state, lease owner, lease expiry, attempt count and outcome persist independently of worker/API process lifetime.
   - Intentional API restart plus stale-lease recovery passed in PR CI #150 and exact-main #151.

6. **Cross-device/server-mediated preview delivery — PASS.**
   - Preview and derivative bytes are served through Central API endpoints and the Web server-side Central API proxy.
   - Actual `MAM.Web` acceptance retrieves the processed video preview and verifies its SHA-256 without exposing Primary Storage credentials.

7. **Asset Details and Processing Queue expose authoritative live state — PASS.**
   - Windows Asset Details/Processing Queue use `MamProcessingApiClient` through Central API only.
   - Web Asset Details/queue/preview surfaces use the server-side Central API proxy with explicit loading/empty/error/retry/degraded/permission states.

8. **Arabic RTL / English LTR premium responsive regressions remain green — PASS.**
   - Windows rendered acceptance succeeded in PR CI #150 and exact-main #151.
   - Web rendered acceptance succeeded in PR CI #150 and exact-main #151.
   - Existing P01–P03 UI/regression gates remained green.

9. **Client/storage/database/worker security boundaries remain intact — PASS.**
   - `eng/p04-client-worker-boundary-acceptance.sh` proves Desktop/Web contain no direct SQL, Primary Storage adapter/credential, Worker project or process-launch path.
   - Repository secret baseline and dependency vulnerability baseline both passed in PR #150 and exact-main #151.

10. **Relevant build/tests/security and exact-main CI are green — PASS.**
    - Build, P00 foundation, P01 UI/rendered, P02 SQL/API/two-client/boundary, P03 upload/storage-boundary, P04 runtime/boundary, secret scan and vulnerability scan all passed on exact-main run #151.

## Runtime failure repaired during P04

The first complete P04 runtime acceptance exposed a real cross-process storage-root defect: the Development `Storage.Primary.Root` was relative and could resolve differently between API and Worker processes. The file-system adapter now canonicalizes relative roots from an explicit `MAM_STORAGE_BASE_PATH` or the directory of `MAM_CONFIG_PATH`, so independent server processes share one deterministic storage root. The same acceptance that previously failed with `InvalidDataException` passed end-to-end on PR CI #150 and exact-main #151.

## Delivered P04 capabilities

- FFprobe technical inspection persisted in SQL Server;
- versioned processing profiles;
- durable SQL processing jobs with lease/heartbeat/attempt/stale recovery;
- video proxy generation;
- image preview generation;
- audio preview generation;
- PDF server-mediated inline preview strategy;
- deterministic derivative identity/path and SHA-256/size verification;
- explicit failure/retry/recovery semantics;
- processing audit events;
- Windows Asset Details and Processing Queue integration;
- Web Asset Details, queue and preview integration;
- cross-device preview through the server boundary;
- canonical shared file-system root semantics for multi-process API/Worker operation;
- automated P04 runtime/security/rendered acceptance in CI.

## Scope not falsely claimed

- Production proxy/preview quality parameters and processing capacity remain site-specific inputs and are not represented as production-accepted by P04.
- Backup Storage verification and the `Protected` invariant remain P06 work.
- Search, collections and metadata curation are P05 work.

P04 is closed. P05 may become the single ACTIVE phase only through the governance transition that references this closure record.

`UNPUSHED_WORK=NONE`
