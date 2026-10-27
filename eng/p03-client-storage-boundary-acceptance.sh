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

# Large Web uploads must never buffer/hash the complete browser File before the
# resumable session starts. Full-file integrity is finalized server-side; only
# bounded chunks are read and SHA-256 checked in the browser.
if grep -q 'p03HashBlob(file)' src/MAM.Web/wwwroot/p03-upload.js; then
  echo "FAIL: P03 Web uploader hashes the complete File in browser memory." >&2
  exit 1
fi
if grep -Eq 'p03HashBlob\((f|file)\)' src/MAM.Web/wwwroot/p127-enterprise-v2.js; then
  echo "FAIL: active P127 Web uploader hashes the complete File in browser memory." >&2
  exit 1
fi
if grep -q 'sha256Hex(file)' src/MAM.Web/wwwroot/t22-tape-management.js; then
  echo "FAIL: T2.2 attachment uploader hashes the complete File in browser memory." >&2
  exit 1
fi

for client in src/MAM.Web/wwwroot/p03-upload.js src/MAM.Web/wwwroot/p127-enterprise-v2.js src/MAM.Web/wwwroot/t22-tape-management.js; do
  if ! grep -q 'expectedSha256:null' "$client"; then
    echo "FAIL: large-file Web flow is not using deferred server-side final SHA-256: $client" >&2
    exit 1
  fi
done

if ! grep -q 'p03SelectedFile' src/MAM.Web/wwwroot/p03-upload.js ||    ! grep -q 'p03SelectedFile' src/MAM.Web/wwwroot/p127-enterprise-v2.js; then
  echo "FAIL: drag/drop File reference is not retained independently from input.files." >&2
  exit 1
fi
if ! grep -q 'p03PutChunkWithRetry' src/MAM.Web/wwwroot/p03-upload.js ||    ! grep -q 'p03PutChunkWithRetry' src/MAM.Web/wwwroot/p127-enterprise-v2.js; then
  echo "FAIL: Web large-file chunk retry/resume guard is missing." >&2
  exit 1
fi
if ! grep -q 'NormalizeExpectedSha' src/MAM.Infrastructure/Uploads/DurableUploadService.cs ||    ! grep -q 'checksumDeferred' src/MAM.Infrastructure/Uploads/DurableUploadService.cs; then
  echo "FAIL: server-side deferred final checksum verification is missing." >&2
  exit 1
fi

if command -v node >/dev/null 2>&1; then
  node --check src/MAM.Web/wwwroot/p03-upload.js
  node --check src/MAM.Web/wwwroot/p127-enterprise-v2.js
  node --check src/MAM.Web/wwwroot/t22-tape-management.js
fi

echo "PASS: Desktop/Web keep the Central API boundary; Web drag/drop uploads are chunked/resumable and avoid full-file browser buffering."
