#!/usr/bin/env bash
set -euo pipefail

clients=(src/MAM.Desktop src/MAM.Web)
for root in "${clients[@]}"; do
  if grep -RInE --exclude-dir=bin --exclude-dir=obj --exclude='*.map' \
    'Microsoft\.Data\.SqlClient|SqlConnection|FileSystemStorageObjectStore|IStorageObjectStore|MAM_SECRET_|ConnectionString|ProcessStartInfo|System\.Diagnostics\.Process|MAM\.Infrastructure|MAM\.Worker' "$root"; then
    echo "FAIL: P05 client contains forbidden direct SQL/storage/worker boundary token: $root" >&2
    exit 1
  fi
done

if grep -RInE --exclude-dir=bin --exclude-dir=obj 'ProjectReference.*MAM\.(Infrastructure|Worker)' src/MAM.Desktop src/MAM.Web; then
  echo "FAIL: Desktop/Web must not reference Infrastructure or Worker projects." >&2
  exit 1
fi

for token in \
  'MamCurationApiClient' \
  '/client-api/curation/search' \
  '/client-api/curation/collections' \
  'BulkUpdateMetadataAsync' \
  'SetArchivedAsync' \
  'SavedFiltersSupported' \
  'curation.metadata.updated'; do
  if ! grep -RIsq --exclude-dir=bin --exclude-dir=obj -- "$token" src eng; then
    echo "FAIL: expected P05 Central API boundary evidence token missing: $token" >&2
    exit 1
  fi
done

echo "PASS: P05 Desktop/Web search and curation remain Central-API-only; no direct SQL, Primary Storage adapter/credential, Infrastructure or Worker process path exists in clients."
