#!/usr/bin/env bash
set -euo pipefail

fail() { echo "P09 client/diagnostics boundary acceptance FAILED: $*" >&2; exit 1; }

for root in src/MAM.Web src/MAM.Desktop; do
  if grep -RInE 'Microsoft\.Data\.SqlClient|SqlConnection|SqlServerConnectionFactory|FileSystemStorageObjectStore|EnvironmentSecretResolver|ResolveRequired\(|MAM_SECRET_DATABASE|MAM_SECRET_PRIMARY_STORAGE|MAM_SECRET_BACKUP_STORAGE' "$root" --exclude-dir=obj --exclude-dir=bin; then
    fail "Client source gained direct SQL/storage/secret authority: $root"
  fi
done

[[ -f src/MAM.Application/Clients/MamOperationsApiClient.cs ]] || fail "Shared operations API client missing."
[[ -f src/MAM.Infrastructure/Operations/SqlServerOperationsService.cs ]] || fail "Authoritative SQL operations service missing."
[[ -f src/MAM.Api/P09Operations.cs ]] || fail "P09 Central API surface missing."
[[ -f src/MAM.Web/P09OperationsProxy.cs ]] || fail "P09 Web operations proxy missing."
[[ -f src/MAM.Web/wwwroot/p09-operations.js ]] || fail "P09 Web operations workspace missing."
[[ -f src/MAM.Desktop/MainWindow.P09.cs ]] || fail "P09 Desktop operations workspace missing."

grep -q 'MamOperationsApiClient' src/MAM.Web/P09OperationsProxy.cs || fail "Web does not use shared Central API operations client."
grep -q 'MamOperationsApiClient' src/MAM.Desktop/MainWindow.P09.cs || fail "Desktop does not use shared Central API operations client."
grep -q 'X-Correlation-ID' src/MAM.Application/Clients/MamOperationsApiClient.cs || fail "Operations client correlation header missing."
grep -q 'correlationId' src/MAM.Worker/Program.cs || fail "Worker serialized correlation field missing."
grep -q 'processing-{job.JobId:N}' src/MAM.Worker/Program.cs || fail "Processing-job stable correlation identity missing."
grep -q 'backup-{backup.JobId:N}' src/MAM.Worker/Program.cs || fail "Backup-job stable correlation identity missing."

if grep -RInE '\.Root\b|CredentialRef|ConnectionStringSecretRef|MAM_SECRET_|Password\s*=|ClientSecret\s*=|ApiKey\s*=' src/MAM.Api/P09Operations.cs src/MAM.Application/Operations src/MAM.Application/Clients/MamOperationsApiClient.cs src/MAM.Web/P09OperationsProxy.cs src/MAM.Web/wwwroot/p09-operations.js src/MAM.Desktop/MainWindow.P09.cs; then
  fail "P09 client/diagnostics surface references a forbidden root, credential ref or secret source."
fi

if grep -RInE 'Process\.Start|ProcessStartInfo' src/MAM.Web/P09OperationsProxy.cs src/MAM.Desktop/MainWindow.P09.cs; then
  fail "P09 reporting clients may not spawn helper/privileged processes."
fi

grep -q 'No database connection strings or resolved secret values are included' src/MAM.Api/P09Operations.cs || fail "Diagnostics redaction policy missing database/secret guarantee."
grep -q 'No storage filesystem roots or credential references are included' src/MAM.Api/P09Operations.cs || fail "Diagnostics redaction policy missing storage-root guarantee."
grep -q 'Reports, Monitoring, Resilience' src/MAM.Web/wwwroot/p09-operations.js || fail "Web P09 operations heading missing."
grep -q 'التقارير والمراقبة والتعافي' src/MAM.Web/wwwroot/p09-operations.js || fail "Arabic Web P09 operations states missing."
grep -q 'Loading' src/MAM.Web/wwwroot/p09-operations.js || fail "Web loading state missing."
grep -q 'Permission denied' src/MAM.Web/wwwroot/p09-operations.js || fail "Web permission state missing."
grep -q 'Degraded' src/MAM.Web/wwwroot/p09-operations.js || fail "Web degraded state missing."
grep -q 'API error' src/MAM.Web/wwwroot/p09-operations.js || fail "Web API error state missing."
grep -q 'التقارير والمراقبة والتعافي' src/MAM.Desktop/MainWindow.P09.cs || fail "Arabic Desktop P09 operations states missing."
grep -q 'Permission denied' src/MAM.Desktop/MainWindow.P09.cs || fail "Desktop permission state missing."
grep -q 'API error' src/MAM.Desktop/MainWindow.P09.cs || fail "Desktop API error state missing."

# Capture reporting must not fabricate a central hardware session authority. Only the post-capture durable upload handoff is reported centrally.
grep -q 'CaptureUploadHandoff' src/MAM.Infrastructure/Operations/SqlServerOperationsService.cs || fail "Capture-to-central handoff reporting missing."
if grep -RInE 'ICaptureProvider|SimulatedCaptureProvider|CaptureRecoveryManifestStore' src/MAM.Api/P09Operations.cs src/MAM.Infrastructure/Operations src/MAM.Web/P09OperationsProxy.cs; then
  fail "P09 central reporting improperly owns the Windows capture provider/recovery cache."
fi

echo "P09 client, diagnostics and platform boundary acceptance: PASS"
