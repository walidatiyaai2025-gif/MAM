# ADR 0005 — Authentication and authorization boundary

Status: Accepted for P02 implementation

## Context

The MAM must support an authoritative Central API, multiple clients, future site-specific identity integration and server-side authorization. Production identity technology is not yet fixed; acceptable site choices may include Active Directory, OIDC or an approved local identity deployment. Desktop and Web must not become security authorities and must not receive SQL Server credentials.

## Decision

1. ASP.NET Core authentication and authorization are the authoritative request-security boundary for the Central API.
2. Application permissions are stable internal claims (`mam.permission`) independent of the external identity provider.
3. Initial roles are `Administrator`, `CatalogEditor` and `Viewer`; role-to-permission mapping is centralized in the Application layer.
4. Protected API operations require explicit server-side authorization policies. Hiding a UI action is never accepted as authorization evidence.
5. Production identity providers will map authenticated external/local identities to internal MAM users, roles and permissions. Provider-specific subjects are not domain identifiers.
6. The Development environment may use the `X-MAM-Dev-User` header only when `Environment.Name=Development` and `Auth.Mode=Local`. The fixed aliases are non-secret test fixtures (`admin`, `editor`, `viewer`). This mechanism is not a production authentication mode.
7. Outside that exact Development condition, the development header authenticator returns no identity. Protected endpoints therefore fail closed until an approved production authentication provider is configured.
8. Catalog persistence follows the same fail-closed principle: the non-production memory catalog is Development-only and cannot satisfy SQL Server or production acceptance.
9. Authentication/session secrets, SQL credentials and provider client secrets remain external secret references; none are committed to source control.

## Consequences

- API tests can prove 401/403 behavior and permission separation before site identity is selected.
- Desktop and Web can integrate against one security contract without provider-specific code.
- P02 is not complete until the SQL Server catalog runtime/migrations and the selected supported authentication runtime have their required acceptance evidence.
- Production readiness remains blocked rather than silently falling back to development identity or local-authoritative data.
