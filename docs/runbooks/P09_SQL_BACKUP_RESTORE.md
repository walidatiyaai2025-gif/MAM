# P09 SQL Server Backup / Restore Runbook

## Scope

This runbook defines the repository-supported **non-production** proof for database backup and restore. It does not select the Diwan Al Amiri production SQL topology, HA technology, backup destination, retention schedule, encryption/key custody, RPO or RTO. Those are site/owner decisions carried to P12 and require real infrastructure evidence.

## Invariant

The SQL catalog is authoritative for asset identity, upload state, processing jobs, protection state, policy/audit state and operational reporting. A successful restore must preserve the same authoritative identities and durable state; media bytes remain on server-managed Primary/Backup storage and are not copied by the database backup itself.

## Automated non-production acceptance

CI executes `tests/MAM.P09.DrAcceptance.Checks` against the ephemeral SQL Server after P02–P09 runtime acceptance has populated the database. The check:

1. records a deterministic signature of `MediaAsset`, `MamMediaOriginal` and `MamSchemaVersion`;
2. creates a `COPY_ONLY` SQL Server full backup with checksum;
3. executes `RESTORE VERIFYONLY ... WITH CHECKSUM`;
4. restores to a separate database name and separate data/log files;
5. recomputes the authoritative signature;
6. fails if asset count, original count/bytes, migration count, asset identity/version checksum or original identity/hash checksum differs;
7. removes the temporary restored database.

No connection string, SQL password, storage credential or production endpoint is written to the repository or acceptance output.

## Production handoff — P12 owner-last

Before production approval, authorized site operators must provide and execute an approved procedure covering:

- SQL Server deployment/HA topology;
- encrypted backup destination and access model;
- full/differential/log backup schedule as applicable;
- retention and immutable/off-site requirements;
- backup monitoring and alert destination;
- key/certificate custody and recovery;
- approved RPO/RTO;
- restore rehearsal on representative infrastructure;
- post-restore application validation against Primary/Backup storage;
- documented rollback and escalation contacts.

P09 automation proves restore mechanics in a controlled non-production environment only. It must not be represented as production DR acceptance.
