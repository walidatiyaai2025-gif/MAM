# ADR 0001 — Centralized client/server and permanent-storage model

- Status: Accepted
- Date: 2026-09-10
- Phase: P00

## Context

The product must run from multiple Windows workstations and browsers while keeping one authoritative catalog and durable institutional media archive. Historical/local reference assumptions must not make a workstation the permanent archive.

## Decision

Use a Central MAM API as the sole business/security boundary for Desktop and Web. SQL Server is the authoritative catalog/state database and is server-side only. Permanent media is written through server-managed storage adapters to Primary Storage. Backup Storage is a distinct target and never becomes an implicit normal-write fallback.

Windows tape capture may use a temporary local ingest cache strictly for capture continuity and recovery. That cache is not authoritative and is not eligible to define an asset as protected.

`Protected` requires independently readable Primary and Backup originals with matching SHA-256 evidence and committed catalog state.

## Consequences

- Desktop/Web never receive SQL credentials.
- Local-only SQLite/folder-library implementations are rejected for production architecture.
- Storage identity is a stable target ID plus managed object key, not a raw client path.
- Later SMB/NAS/SAN/object implementations remain behind `IStorageObjectStore`.
- Recovery/failover is explicit and auditable rather than silent.
