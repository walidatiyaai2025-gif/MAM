# P03 — Primary Storage & Durable Upload — Closure Evidence

Status: **CLOSED**

P03 closed only after the implementation PR was green, merged, and the full phase-exit workflow succeeded again on exact `main`.

## Verified integration identity

- Implementation PR: **#11 — P03: Primary Storage and durable resumable upload**
- Validated PR head: `4ea53febbccac5fd10233f6618151251397c4988`
- PR CI: run **#139** / `34647039559` — **SUCCESS**
- Implementation merge SHA: `8d48d0aae94f55953edabe06f26dd2c48bb8baba`
- Exact-main phase-exit CI: run **#140** / `34647242488` — **SUCCESS**

## Exit-gate mapping

1. **Large interrupted upload resumes without restarting from zero — PASS.**
   - `eng/p03-upload-acceptance.sh` uploads the first 16 MiB chunk, stops the API, restarts it, reads the persisted SQL session offset, and resumes from the acknowledged offset.
   - The P03 durable Primary upload step passed in PR CI #139 and exact-main CI #140.

2. **Server verifies final size and SHA-256 before accepting the Primary original — PASS.**
   - `DurableUploadService.FinalizeAsync` measures the server staging object, rejects length/hash mismatch, writes through the Primary adapter, and re-verifies the written object before catalog promotion.
   - Acceptance compares the materialized Primary object byte length and SHA-256 against the original source.

3. **Authoritative original is server-managed Primary Storage, not client-local permanent media — PASS.**
   - Permanent original promotion occurs only through `IStorageObjectStore` / `FileSystemStorageObjectStore` on the server.
   - Desktop/Web only use the Central API upload protocol; their local selection/cache is non-authoritative.

4. **Desktop/Web have no direct Primary Storage credential/access path — PASS.**
   - `eng/p03-client-storage-boundary-acceptance.sh` passed in PR #139 and exact-main #140.
   - Client projects contain no direct SQL/Primary adapter/credential path.

5. **Unsafe/traversal paths and invalid file inputs fail closed — PASS.**
   - Original file names are normalized and paths/traversal are rejected.
   - Chunk hash mismatches fail without advancing the durable committed offset.
   - Unsupported extensions follow the configured quarantine path and cannot be promoted to Primary.

6. **Duplicate policy is deterministic and tested — PASS.**
   - Duplicate authoritative SHA-256 returns conflict with the existing asset identity.

7. **Dependency failures expose recoverable/degraded states without silent data loss — PASS.**
   - API restart recovery preserves the committed upload offset.
   - Injected Primary Storage failure produces explicit degraded storage health rather than false readiness.
   - Windows/Web expose retry/degraded/permission/error/progress states.

8. **Same ingested asset is visible through the Central API to Desktop and Web — PASS.**
   - The Windows-identity upload is observed through the Web/catalog identity path.
   - The actual `MAM.Web` server-side proxy uploads a second asset that is then visible through the authoritative Central API catalog.

9. **Arabic/English premium responsive UI and upload states remain intact — PASS.**
   - Windows rendered acceptance passed in PR CI #139 and exact-main #140.
   - Web rendered acceptance passed in PR CI #139 and exact-main #140.
   - P01 UI contract regression remained green.

10. **Build/tests/security and exact-main CI green — PASS.**
    - Build, P00 foundation, P01 UI, P02 API/SQL/two-client/boundary regression, P03 durable upload, P03 storage boundary, repository secret baseline, dependency vulnerability baseline, Windows rendered and Web rendered all passed on exact-main run #140.

## Delivered P03 capabilities

- server-managed Primary Storage adapter and health/readiness;
- SQL-persisted upload sessions, chunk receipts and Primary-original records via `0003_p03_durable_upload.sql`;
- resumable offset-based chunk protocol with per-chunk SHA-256;
- final server-side size + SHA-256 verification before Primary promotion;
- strict file-name/path validation and traversal prevention;
- deterministic duplicate detection;
- quarantine behavior for unknown file types;
- server staging recovery after API interruption;
- catalog creation only after verified Primary write;
- Windows Upload integration using the Central API;
- Web Upload integration through its server-side Central API proxy;
- explicit upload progress/retry/error/degraded/permission states;
- automated P03 acceptance in CI.

## Scope not falsely claimed

- Production Primary Storage endpoint/type/capacity/service identity remains site-specific and is not represented as production-accepted by this phase.
- Backup Storage verification and the `Protected` invariant remain P06 work.
- Media inspection/proxies/previews are P04 work.

P03 is closed. P04 may become the single ACTIVE phase only through the governance transition that references this closure record.

`UNPUSHED_WORK=NONE`
