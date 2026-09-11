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
web_pid=""
cleanup_main() {
  if [[ -n "$web_pid" ]]; then kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; fi
  kill "$api_pid" >/dev/null 2>&1 || true
  wait "$api_pid" >/dev/null 2>&1 || true
}
trap cleanup_main EXIT

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

dotnet run --project tests/MAM.P02.ClientAcceptance.Checks/MAM.P02.ClientAcceptance.Checks.csproj --configuration Release --no-build -- "$base_url"

# Launch the actual MAM.Web executable configured only with the Central API endpoint.
web_url="http://127.0.0.1:5095"
web_log="${RUNNER_TEMP:-/tmp}/mam-p02-web-proxy.log"
export MAM_API_BASE_URL="$base_url"
export MAM_DEV_USER="editor"
export ASPNETCORE_URLS="$web_url"
dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$web_log" 2>&1 &
web_pid=$!
web_ready=0
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep 0.5
done
if [[ "$web_ready" -ne 1 ]]; then
  cat "$web_log"
  echo "FAIL: MAM.Web did not become ready for P02 proxy acceptance." >&2
  exit 1
fi

web_created=$(curl --fail --silent -H 'Content-Type: application/json' --data '{"title":"P02 Web Executable Asset"}' "$web_url/client-api/catalog/assets")
web_asset_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$web_created")
[[ -n "$web_asset_id" ]] || { echo "FAIL: Web executable did not return a created asset." >&2; exit 1; }
api_view=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$base_url/api/v1/catalog/assets")
python3 -c 'import json,sys; expected=sys.argv[1]; data=json.load(sys.stdin); assert any(str(x["id"])==expected and x["title"]=="P02 Web Executable Asset" for x in data)' "$web_asset_id" <<<"$api_view"
web_schemas=$(curl --fail --silent "$web_url/client-api/metadata/schemas")
python3 -c 'import json,sys; data=json.load(sys.stdin); assert any(x["key"]=="core-media-v1" for x in data)' <<<"$web_schemas"

echo "PASS: actual MAM.Web executable reads/writes the shared SQL-backed catalog only through the Central API proxy."

kill "$web_pid" >/dev/null 2>&1 || true
wait "$web_pid" >/dev/null 2>&1 || true
web_pid=""

audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$base_url/api/v1/audit/recent?limit=50")
python3 -c 'import json,sys; events=json.load(sys.stdin); actions={e["action"] for e in events}; required={"catalog.asset.created","catalog.asset.title-updated","catalog.asset.title-update-conflict"}; assert required.issubset(actions), (required,actions)' <<<"$audit"

cleanup_main
trap - EXIT

# Prove dependency degradation is observable over HTTP without pretending readiness.
unset MAM_APPLY_MIGRATIONS
unset MAM_API_BASE_URL
unset MAM_DEV_USER
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
