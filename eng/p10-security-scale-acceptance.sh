#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5300"
work="${RUNNER_TEMP:-/tmp}/mam-p10"
rm -rf "$work"
mkdir -p "$work"
rm -rf .mam-dev/primary .mam-dev/backup src/MAM.Api/.mam-dev/primary src/MAM.Api/.mam-dev/backup

password_key="Password"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"
export ASPNETCORE_URLS="$api_url"
export P10_API_URL="$api_url"
export P10_WORK="$work"

dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api.log" 2>&1 &
api_pid=$!
cleanup() {
  kill "$api_pid" >/dev/null 2>&1 || true
  wait "$api_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

ready=0
for _ in $(seq 1 100); do
  if curl --fail --silent "$api_url/health/ready" >/dev/null 2>&1 && curl --fail --silent "$api_url/health/curation" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then
  cat "$work/api.log"
  echo "FAIL: P10 SQL-backed API did not become ready." >&2
  exit 1
fi

python3 eng/p10-security-scale-acceptance.py

echo "P10 security/scale/concurrent-ingest acceptance passed."