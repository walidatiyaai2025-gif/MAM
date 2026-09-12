# P09 Configuration & Key-Reference Recovery

## Purpose

Recover MAM configuration without turning configuration backup into a secret store. Repository/runtime configuration may contain **opaque references** to credentials or keys; plaintext passwords, API keys, tokens, private keys and resolved secret values must remain in the authorized external secret provider.

## What to back up

- the deployed, validated MAM configuration files;
- application version and exact commit/release identity;
- environment/site identifiers;
- SQL migration version;
- Primary/Backup **target IDs and policy identifiers**;
- secret-reference identifiers such as environment/provider reference names;
- identity-provider configuration identifiers and claim mappings after site approval;
- retention, logging, diagnostics and notification policy identifiers;
- deployment manifests and artifact SHA-256 values.

## What must never be included

- resolved database connection strings;
- SQL passwords;
- storage passwords/access keys;
- OAuth/client secrets or bearer tokens;
- private certificates/private keys;
- raw filesystem roots in client diagnostics bundles;
- captured production media or personal metadata solely for support convenience.

## Recovery sequence

1. restore the approved application/release artifacts by exact version/hash;
2. restore validated configuration containing only non-secret values and opaque secret references;
3. restore/rebind the referenced secrets through the authorized external secret mechanism;
4. validate SQL, Primary and Backup dependency health server-side;
5. validate identity/authorization bindings;
6. validate processing/protection queues and stale-job recovery;
7. verify a representative original using stored length/SHA-256 and confirm protection state remains fail-closed;
8. generate the P09 diagnostics bundle and confirm no resolved secret material is present;
9. document the recovery evidence and any remaining degraded dependency.

## Key/certificate owner-last boundary

Actual production key escrow, certificate backup, HSM/KMS procedures, rotation authority and break-glass access are P12 site/security approvals. P09 provides the software boundary and documented recovery sequence only; absence of real key-custody evidence is not PASS.
