# Owner-Last Production Acceptance Policy

## Purpose

Keep engineering execution non-blocking while preserving truthful production acceptance. Human/site/external dependencies are executed at the end in P12 rather than interrupting P07–P11 whenever the repository can continue safely without them.

## Rule

For P07–P11, a phase may close its **engineering scope** when all repository/cloud-actionable implementation, automated acceptance, documentation, CI, security and exact-main integration requirements are complete. A requirement may be transferred to P12 only when it intrinsically depends on a real target site, physical equipment, production-only credential/endpoint, approved business policy, or authorized human sign-off that cloud execution cannot lawfully fabricate.

Transferred items use status `DEFERRED_TO_P12 / OWNER_LAST`. That status is never PASS and never proves production readiness.

## Required transfer record

Every deferred item must record:

1. originating phase and requirement;
2. why it cannot be evidenced by repository/cloud execution;
3. exact owner/site action;
4. exact evidence required for PASS;
5. affected production risk if omitted;
6. prepared validator/checklist/runbook where technically possible.

## P12 consolidated owner-last categories

- exact tape deck/capture card/driver/runtime and physical capture certification;
- approved preservation/capture profile and dropped-frame threshold;
- target DNS/TLS/network/firewall/NTP readiness;
- production SQL topology, HA/backup and service identity;
- Primary/Backup endpoints, capacity, permissions and physical-independence topology;
- production identity-provider configuration and credentials;
- private signing certificate/key where signing is required;
- approved retention, deletion, audit, notification, RPO/RTO and disaster-recovery policies;
- target concurrency/throughput acceptance target if business/site approval is required;
- target-device/browser/workstation UAT;
- authorized production deployment/go-live sign-off.

## Non-deferrable engineering work

The following cannot be moved to P12 merely for convenience:

- code or tests that can be implemented in the repository;
- fail-closed configuration/secret-reference behavior;
- validators and deployment checks;
- simulation/non-production recovery testing;
- security negative testing;
- packaging/release automation;
- migration/upgrade/uninstall preservation tests;
- diagnostics and health surfaces;
- responsive Arabic RTL/English LTR UI behavior;
- exact-main regression CI.

## Final project closure

P00–P11 may be engineering-CLOSED while P12 remains ACTIVE. The project is then implementation-complete but **not production-approved**. Only P12 closure with real owner/site evidence permits `PRODUCTION_READY`, `GO_LIVE_APPROVED`, or equivalent final-completion claims.
