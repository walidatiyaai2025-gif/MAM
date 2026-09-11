#!/usr/bin/env bash
set -euo pipefail

for client in src/MAM.Desktop src/MAM.Web; do
  if grep -RInE --include='*.cs' --include='*.csproj' \
    'Microsoft\.Data\.SqlClient|SqlConnection|IStorageObjectStore|FileSystemStorageObjectStore|Storage\.Primary|CredentialRef|PrimaryStorageTargetSettings' "$client"; then
    echo "FAIL: client contains a direct database/Primary Storage implementation or credential boundary reference: $client" >&2
    exit 1
  fi
done

if grep -RInE --include='*.cs' --include='*.js' 'MAM_SECRET_|ConnectionStringSecretRef' src/MAM.Desktop src/MAM.Web; then
  echo "FAIL: client contains server-side secret reference/runtime material." >&2
  exit 1
fi

if ! grep -q 'MamUploadApiClient' src/MAM.Desktop/MainWindow.P03.cs; then
  echo "FAIL: Windows Upload workflow is not wired through the shared Central API client." >&2
  exit 1
fi
if ! grep -q '/client-api/uploads/sessions' src/MAM.Web/wwwroot/p03-upload.js; then
  echo "FAIL: Web Upload workflow is not wired through its Central API proxy." >&2
  exit 1
fi

echo "PASS: Desktop/Web have no direct SQL/Primary Storage credential or adapter path; both P03 workflows use the Central API boundary."
