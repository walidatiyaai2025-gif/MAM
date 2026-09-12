#!/usr/bin/env bash
set -euo pipefail

fail() { echo "P08 client boundary acceptance FAILED: $*" >&2; exit 1; }

for root in src/MAM.Web src/MAM.Desktop; do
  if grep -RInE 'Microsoft\.Data\.SqlClient|SqlConnection|SqlServerConnectionFactory|FileSystemStorageObjectStore|EnvironmentSecretResolver|ResolveRequired\(|MAM_SECRET_PRIMARY_STORAGE|MAM_SECRET_BACKUP_STORAGE|MAM_SECRET_AUTH_PRODUCTION' "$root" --exclude-dir=obj --exclude-dir=bin; then
    fail "Client source gained direct SQL/storage/secret resolution authority: $root"
  fi
done

if grep -RInE 'Password\s*=|ClientSecret\s*=|ApiKey\s*=|ConnectionString\s*=' src/MAM.Web src/MAM.Desktop --exclude-dir=obj --exclude-dir=bin; then
  fail "Client source contains a plaintext credential-shaped assignment."
fi

[[ -f src/MAM.Application/Clients/MamAdministrationApiClient.cs ]] || fail "Shared administration API client missing."
[[ -f src/MAM.Web/P08AdministrationProxy.cs ]] || fail "Web administration proxy missing."
[[ -f src/MAM.Web/wwwroot/p08-administration.js ]] || fail "Web administration workspace missing."
[[ -f src/MAM.Desktop/MainWindow.P08.cs ]] || fail "Desktop administration workspace missing."

grep -q 'MamAdministrationApiClient' src/MAM.Web/P08AdministrationProxy.cs || fail "Web does not use the shared Central API administration client."
grep -q 'MamAdministrationApiClient' src/MAM.Desktop/MainWindow.P08.cs || fail "Desktop does not use the shared Central API administration client."
grep -q 'SecretRef' src/MAM.Web/wwwroot/p08-administration.js || fail "Web does not present secret-reference-only administration semantics."
grep -q 'secret material is never redisplayed\|resolved secret values never return' src/MAM.Web/wwwroot/p08-administration.js || fail "Web does not communicate non-redisplay of secrets."
grep -q 'جاري\|إدارة المؤسسة' src/MAM.Web/wwwroot/p08-administration.js || fail "Arabic administration states missing from Web."
grep -q 'Permission denied' src/MAM.Web/wwwroot/p08-administration.js || fail "Web permission state missing."
grep -q 'Degraded' src/MAM.Web/wwwroot/p08-administration.js || fail "Web degraded state missing."
grep -q 'Conflict' src/MAM.Web/wwwroot/p08-administration.js || fail "Web optimistic-concurrency conflict state missing."
grep -q 'API error' src/MAM.Web/wwwroot/p08-administration.js || fail "Web API error state missing."
grep -q 'Loading' src/MAM.Web/wwwroot/p08-administration.js || fail "Web loading state missing."
grep -q 'إدارة المؤسسة والسياسات' src/MAM.Desktop/MainWindow.P08.cs || fail "Arabic administration states missing from Desktop."
grep -q 'Permission denied' src/MAM.Desktop/MainWindow.P08.cs || fail "Desktop permission state missing."
grep -q 'Conflict' src/MAM.Desktop/MainWindow.P08.cs || fail "Desktop conflict state missing."

if grep -RInE 'MAM\.Infrastructure\.(Catalog|Storage|Secrets|Administration)' src/MAM.Web src/MAM.Desktop --exclude-dir=obj --exclude-dir=bin; then
  fail "Client source references server infrastructure for P08 authority."
fi

# P07 Windows capture runtime is intentionally client-side and isolated; P08 must not broaden it into server authority.
if grep -RInE 'Process\.Start|ProcessStartInfo' src/MAM.Web/P08AdministrationProxy.cs src/MAM.Desktop/MainWindow.P08.cs; then
  fail "P08 administration clients may not spawn privileged/helper processes."
fi

# Production policy data may contain opaque references, but committed configuration and UI may not carry resolved credentials.
if grep -RInE 'p08-ci-secret-material-never-return-this-value|NeverPersistMe' src config database --exclude-dir=obj --exclude-dir=bin; then
  fail "Test secret material leaked into production/client/configuration source."
fi

echo "P08 client Central API/secret boundary acceptance: PASS"
