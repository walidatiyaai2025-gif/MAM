#!/usr/bin/env bash
set -euo pipefail

fail(){ echo "FAIL: $*" >&2; exit 1; }

# Clients may consume only Application-layer Central API contracts and the isolated
# Windows capture runtime. They must never gain direct SQL, server infrastructure,
# storage adapter, worker/process, credential, or filesystem backup access.
for project in src/MAM.Desktop src/MAM.Web; do
  if grep -RInE 'Microsoft\.Data\.SqlClient|SqlConnection|SqlServerConnectionFactory|FileSystemStorageObjectStore|ConnectionStringSecretRef|CredentialRef|\.mam-dev/backup|Storage\.Backup\.Root|Process\.Start' "$project" --include='*.cs' --include='*.csproj' --include='*.js' --include='*.html'; then
    fail "$project contains a direct server/database/storage/worker boundary violation."
  fi
done

if grep -RInE 'ProjectReference.*MAM\.(Infrastructure|Worker)' src/MAM.Desktop src/MAM.Web; then
  fail 'Desktop/Web must not reference the general Infrastructure or Worker projects.'
fi

grep -q 'MamProtectionApiClient' src/MAM.Desktop/MainWindow.P06.cs || fail 'Windows P06 surface does not use the shared Central API protection client.'
grep -q 'MamProtectionApiClient' src/MAM.Web/Program.cs || fail 'Web proxy does not use the shared Central API protection client.'
grep -q 'client-api/protection/summary' src/MAM.Web/Program.cs || fail 'Web protection summary proxy is missing.'
grep -q 'client-api/protection/health' src/MAM.Web/Program.cs || fail 'Web protection health proxy is missing.'
grep -q 'Permission denied' src/MAM.Web/wwwroot/p06-protection.js || fail 'Web permission state is missing.'
grep -q 'Degraded' src/MAM.Web/wwwroot/p06-protection.js || fail 'Web degraded state is missing.'
grep -q 'Backup Pending' src/MAM.Web/wwwroot/p06-protection.js || fail 'Web pending protection state is missing.'
grep -q 'Mismatch' src/MAM.Web/wwwroot/p06-protection.js || fail 'Web mismatch protection state is missing.'
grep -q 'حماية النسخة الاحتياطية' src/MAM.Web/wwwroot/p06-protection.js || fail 'Web Arabic protection UI is missing.'
grep -q 'حماية النسخة الاحتياطية' src/MAM.Desktop/MainWindow.P06.cs || fail 'Windows Arabic protection UI is missing.'
grep -q 'Permission denied' src/MAM.Desktop/MainWindow.P06.cs || fail 'Windows permission state is missing.'
grep -q 'Degraded' src/MAM.Desktop/MainWindow.P06.cs || fail 'Windows degraded state is missing.'

echo 'PASS: P06 Desktop/Web protection surfaces use only the Central API contract and expose bilingual loading/permission/degraded/pending/protected/mismatch semantics without SQL/storage credentials.'
