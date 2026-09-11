#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
base_url="http://127.0.0.1:5093"
log_file="${RUNNER_TEMP:-/tmp}/mam-p02-sql-api.log"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export ASPNETCORE_URLS="$base_url"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;Password=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=true
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"

dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$log_file" 2>&1 &
api_pid=$!
cleanup() {
  kill "$api_pid" >/dev/null 2>&1 || true
  wait "$api_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

ready=0
for _ in $(seq 1 80); do
  if curl --fail --silent "$base_url/health/ready" >/tmp/p02-sql-ready.json 2>/dev/null; then
    ready=1
    break
  fi
  sleep 0.5
done
if [[ "$ready" -ne 1 ]]; then
  cat "$log_file"
  echo "FAIL: SQL-backed MAM.Api did not become ready." >&2
  exit 1
fi
python3 -c 'import json; d=json.load(open("/tmp/p02-sql-ready.json")); assert d["status"]=="Ready" and d["catalog"]["provider"]=="SqlServer"'

status=$(curl --silent --output /tmp/p02-sql-anon.json --write-out '%{http_code}' "$base_url/api/v1/catalog/assets")
[[ "$status" == "401" ]] || { cat /tmp/p02-sql-anon.json; echo "FAIL: SQL anonymous read expected 401, got $status" >&2; exit 1; }
status=$(curl --silent --output /tmp/p02-sql-viewer-write.json --write-out '%{http_code}' \
  -H 'X-MAM-Dev-User: viewer' -H 'Content-Type: application/json' --data '{"title":"Forbidden"}' \
  "$base_url/api/v1/catalog/assets")
[[ "$status" == "403" ]] || { cat /tmp/p02-sql-viewer-write.json; echo "FAIL: SQL viewer write expected 403, got $status" >&2; exit 1; }

dotnet run --project tests/MAM.P02.ClientAcceptance.Checks/MAM.P02.ClientAcceptance.Checks.csproj --configuration Release -- "$base_url"

audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$base_url/api/v1/audit/recent?limit=50")
python3 -c 'import json,sys; events=json.load(sys.stdin); actions={e["action"] for e in events}; required={"catalog.asset.created","catalog.asset.title-updated","catalog.asset.title-update-conflict"}; assert required.issubset(actions), (required,actions)' <<<"$audit"

cleanup
trap - EXIT

# Prove dependency degradation is observable over HTTP without pretending readiness.
unset MAM_APPLY_MIGRATIONS
export ASPNETCORE_URLS="http://127.0.0.1:5094"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14334;Initial Catalog=Unavailable;User ID=sa;Password=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=1"
bad_log="${RUNNER_TEMP:-/tmp}/mam-p02-sql-degraded.log"
dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$bad_log" 2>&1 &
bad_pid=$!
cleanup_bad() {
  kill "$bad_pid" >/dev/null 2>&1 || true
  wait "$bad_pid" >/dev/null 2>&1 || true
}
trap cleanup_bad EXIT

live=0
for _ in $(seq 1 40); do
  if curl --fail --silent "http://127.0.0.1:5094/health/live" >/dev/null 2>&1; then live=1; break; fi
  sleep 0.5
done
[[ "$live" == "1" ]] || { cat "$bad_log"; echo "FAIL: degraded API did not become live." >&2; exit 1; }
status=$(curl --silent --output /tmp/p02-sql-degraded.json --write-out '%{http_code}' "http://127.0.0.1:5094/health/ready")
[[ "$status" == "503" ]] || { cat /tmp/p02-sql-degraded.json; echo "FAIL: unavailable SQL readiness expected 503, got $status" >&2; exit 1; }
python3 -c 'import json; d=json.load(open("/tmp/p02-sql-degraded.json")); assert d["status"]=="Degraded" and d["catalog"]["provider"]=="SqlServer"'

echo "PASS: SQL-backed Central API enforces authorization, shared two-client state, optimistic concurrency, persistent audit and readiness/degraded health semantics."
