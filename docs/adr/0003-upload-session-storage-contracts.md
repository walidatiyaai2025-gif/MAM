# ADR 0003 — Upload-session and storage-adapter contracts

- Status: Accepted
- Date: 2026-09-10
- Phase: P00

## Decision

Large ingest uses durable upload sessions rather than one long HTTP request. A session carries server-issued identity, asset identity, expected length/hash, chunk size and expiry. Chunk receipts are offset-based and independently verifiable. Finalization verifies total size + SHA-256 before promotion.

Permanent storage access is abstracted through `IStorageObjectStore`. The application addresses a configured storage target by stable ID and server-generated object key. Clients never submit trusted server filesystem paths.

Primary and Backup are separately configured adapters. Backup verification is a later durable workflow and cannot be used to weaken Primary commit semantics.

## Why

This contract supports resumability, crash/restart recovery, deterministic integrity checks and future storage providers while preserving one authoritative server policy boundary.
