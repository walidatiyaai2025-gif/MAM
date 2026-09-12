#!/usr/bin/env bash
set -euo pipefail

clients=(src/MAM.Desktop src/MAM.Web)
for root in "${clients[@]}"; do
  if grep -RInE --exclude-dir=bin --exclude-dir=obj --exclude='*.map' \
    'Microsoft\.Data\.SqlClient|SqlConnection|FileSystemStorageObjectStore|IStorageObjectStore|MAM_SECRET_|ConnectionString|ProcessStartInfo|System\.Diagnostics\.Process|ffmpeg|ffprobe|MAM\.Worker' "$root"; then
    echo "FAIL: client contains a forbidden SQL/Primary Storage/worker/process boundary token: $root" >&2
    exit 1
  fi
done

if grep -RInE --exclude-dir=bin --exclude-dir=obj 'ProjectReference.*MAM\.(Infrastructure|Worker)' src/MAM.Desktop src/MAM.Web; then
  echo "FAIL: Desktop/Web must not reference Infrastructure or Worker projects." >&2
  exit 1
fi

for token in \
  'MamProcessingApiClient' \
  '/client-api/processing/jobs' \
  '/client-api/processing/assets/' \
  'processing.job.completed' \
  'DerivativesPrefix' \
  'LeaseNextAsync' \
  'ArgumentList.Add'; do
  if ! grep -RIsq --exclude-dir=bin --exclude-dir=obj -- "$token" src eng; then
    echo "FAIL: expected P04 processing boundary evidence token missing: $token" >&2
    exit 1
  fi
done

echo "PASS: Desktop/Web reach media processing and preview only through Central API contracts; no direct SQL, Primary Storage credential/adapter, worker project or process-launch path exists in clients."
