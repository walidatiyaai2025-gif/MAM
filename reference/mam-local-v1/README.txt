MAM LOCAL - REFERENCE EXAMPLES - 1.0 - 2026-09-10

Read the accompanying Arabic implementation specification PDF first.
These files illustrate contracts and algorithms. They are NOT the complete MAM
application and are NOT a security accreditation or institutional policy.

schema.sql: executable reference SQLite schema.
desktop-contract.ts: TypeScript interface only; requires native implementation.
media_reference.py: Python 3.10+ argv builders and DB snapshot example.

No institution data, passwords, sample media, credentials or external service
integrations are included. Do not place institutional data in external AI tools.

Verification performed: SQLite schema execution and constraints, SQLite snapshot
integrity, Python syntax, argument construction and validation on synthetic input.
Not performed: actual FFmpeg encoding, full decode, Tauri compilation, TypeScript
type-check, installer generation, endpoint authorization or security assessment.

Production implementation still requires authorization, native path broker,
scanner integration, decoder sandboxing, resource limits, job lifecycle,
transactional publication/recovery, backup manifests, viewer, UI and UAT.

The libx264-enabled FFmpeg build and all dependencies must be approved and their
licenses assessed by the institution. No third-party binaries are distributed.
