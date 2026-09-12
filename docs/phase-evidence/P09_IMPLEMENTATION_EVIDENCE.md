# P09 Implementation Evidence — Reports, Monitoring, Resilience & Disaster Recovery

- **Phase:** `P09 — Reports, Monitoring, Resilience & Disaster Recovery`
- **Engineering status:** `IN PROGRESS`
- **Implementation PR:** `#25`
- **Branch:** `worker/p09-operations-resilience`
- **Production/site DR acceptance:** `DEFERRED_TO_P12 / OWNER_LAST` — **not PASS**

## Authoritative reporting

P09 reports are calculated from existing authoritative durable state rather than a parallel analytics store:

- `MediaAsset` and `MamMediaOriginal` for catalog/original counts and bytes;
- `MamUploadSession` for bounded ingest throughput and post-capture durable-upload handoff;
- `MamProcessingJob` for queued/leased/failed/stale processing visibility;
- `MamBackupJob` for durable protection queue visibility;
- `MamBackupProtection` for Protected/Pending/Failed/Mismatch coverage and integrity-check evidence.

No production capacity, throughput target, alert threshold, RPO or RTO is invented when site values are absent.

## Monitoring and diagnostics

P09 provides Central API operations health, summary, throughput, queue, integrity, safe-storage, dependency-health and diagnostics endpoints. `X-Correlation-ID` is propagated/generated safely, API logging scopes use the correlation identifier, and Worker job logs use deterministic per-job correlation identities.

The diagnostics contract intentionally excludes resolved secrets, database connection strings, client secret material, storage filesystem roots and credential references. Desktop and Web consume only Central API contracts.

## Resilience acceptance

`eng/p09-operations-resilience-acceptance.sh` executes against real API/Web processes and an ephemeral SQL Server. It covers:

- authorization boundaries;
- report contract sanity and bounded throughput validation;
- queue visibility including processing, backup and capture-to-central handoff;
- integrity/protection reporting;
- dependency health;
- secret-safe diagnostics;
- supplied correlation-ID round trip;
- intentional API stop/restart with persisted summary-state equivalence before/after restart;
- injected SQL dependency failure returning explicit degraded/503 state;
- recovery/continued readiness of the healthy path;
- live Web Central API proxy and P09 shell activation.

Existing P04/P06 regression acceptance remains authoritative for Worker crash-after-lease, stale-lease reclaim and durable processing/protection recovery. P09 does not duplicate those state machines.

## SQL backup/restore proof

`tests/MAM.P09.DrAcceptance.Checks` and `docs/runbooks/P09_SQL_BACKUP_RESTORE.md` provide executable non-production SQL backup/restore evidence. The acceptance is designed to:

1. record an authoritative database signature;
2. create a checksum-protected `COPY_ONLY` backup;
3. execute `RESTORE VERIFYONLY`;
4. restore to a separate database/data/log target;
5. compare catalog/original/migration counts and identity/hash checksums;
6. clean the temporary restored database.

The dedicated `.github/workflows/p09-acceptance.yml` runs this proof after runtime/resilience and client-boundary acceptance. Final evidence is recorded only after the workflow succeeds.

## Configuration/key recovery

`docs/runbooks/P09_CONFIGURATION_RECOVERY.md` defines backup/recovery of validated non-secret configuration and opaque key/credential references while explicitly excluding resolved credentials/private key material. Production key escrow/HSM/KMS/certificate custody is P12 owner-last.

## User surfaces

- Windows: premium bilingual `Operations & DR` workspace via `MamOperationsApiClient` only.
- Web: responsive bilingual `Operations & DR` route via server-side Central API proxy only.
- Both surfaces provide loading, permission, degraded and API-error behavior and expose safe target identifiers rather than roots/credentials.

## Remaining closure gates

P09 remains open until:

- dedicated `p09-acceptance` is green on the final PR head, including real non-production SQL restore proof;
- full repository `ci` is green on the same PR head, including P00–P08 regressions, rendered Windows/Web evidence, repository secret scan and dependency vulnerability scan;
- PR #25 is merged;
- both workflows are green again on the exact merged `main` SHA;
- governance/ledger closure is reconciled without converting P12 dependencies to PASS.

`UNPUSHED_WORK=NONE`
