#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5092"
log_file="${RUNNER_TEMP:-/tmp}/mam-p02-api.log"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export ASPNETCORE_URLS="$base_url"

dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$log_file" 2>&1 &
api_pid=$!
cleanup() {
  kill "$api_pid" >/dev/null 2>&1 || true
  wait "$api_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

ready=0
for _ in $(seq 1 60); do
  if curl --fail --silent "$base_url/health/live" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 0.5
done
if [[ "$ready" -ne 1 ]]; then
  cat "$log_file"
  echo "FAIL: MAM.Api did not become ready." >&2
  exit 1
fi

status=$(curl --silent --output /tmp/p02-anon.json --write-out '%{http_code}' "$base_url/api/v1/catalog/assets")
[[ "$status" == "401" ]] || { cat /tmp/p02-anon.json; echo "FAIL: anonymous catalog read expected 401, got $status" >&2; exit 1; }

status=$(curl --silent --output /tmp/p02-viewer-list.json --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' "$base_url/api/v1/catalog/assets")
[[ "$status" == "200" ]] || { cat /tmp/p02-viewer-list.json; echo "FAIL: viewer catalog read expected 200, got $status" >&2; exit 1; }

status=$(curl --silent --output /tmp/p02-viewer-write.json --write-out '%{http_code}' \
  -H 'X-MAM-Dev-User: viewer' \
  -H 'Content-Type: application/json' \
  --data '{"title":"Viewer Must Not Create"}' \
  "$base_url/api/v1/catalog/assets")
[[ "$status" == "403" ]] || { cat /tmp/p02-viewer-write.json; echo "FAIL: viewer catalog write expected 403, got $status" >&2; exit 1; }

created=$(curl --fail --silent \
  -H 'X-MAM-Dev-User: editor' \
  -H 'Content-Type: application/json' \
  --data '{"title":"P02 Acceptance Asset"}' \
  "$base_url/api/v1/catalog/assets")

asset_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$created")
version=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["version"])' <<<"$created")
[[ -n "$asset_id" && "$version" == "1" ]] || { echo "FAIL: create response missing expected id/version: $created" >&2; exit 1; }

viewer_list=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$base_url/api/v1/catalog/assets")
python3 -c 'import json,sys; expected=sys.argv[1]; data=json.load(sys.stdin); assert any(str(item["id"]) == expected and item["title"] == "P02 Acceptance Asset" for item in data)' "$asset_id" <<<"$viewer_list"

status=$(curl --silent --output /tmp/p02-stale.json --write-out '%{http_code}' \
  -X PATCH \
  -H 'X-MAM-Dev-User: editor' \
  -H 'Content-Type: application/json' \
  --data '{"title":"Stale Update","expectedVersion":0}' \
  "$base_url/api/v1/catalog/assets/$asset_id/title")
[[ "$status" == "409" ]] || { cat /tmp/p02-stale.json; echo "FAIL: stale metadata update expected 409, got $status" >&2; exit 1; }

updated=$(curl --fail --silent \
  -X PATCH \
  -H 'X-MAM-Dev-User: editor' \
  -H 'Content-Type: application/json' \
  --data "{\"title\":\"P02 Renamed Asset\",\"expectedVersion\":$version}" \
  "$base_url/api/v1/catalog/assets/$asset_id/title")
updated_version=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["version"])' <<<"$updated")
[[ "$updated_version" == "2" ]] || { echo "FAIL: expected updated version 2, got: $updated" >&2; exit 1; }

status=$(curl --silent --output /tmp/p02-viewer-audit.json --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' "$base_url/api/v1/audit/recent")
[[ "$status" == "403" ]] || { cat /tmp/p02-viewer-audit.json; echo "FAIL: viewer audit read expected 403, got $status" >&2; exit 1; }

audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$base_url/api/v1/audit/recent")
python3 -c 'import json,sys; events=json.load(sys.stdin); actions={event["action"] for event in events}; assert "catalog.asset.created" in actions; assert "catalog.asset.title-updated" in actions' <<<"$audit"

curl --fail --silent "$base_url/health/ready" >/tmp/p02-ready.json
python3 -c 'import json,sys; data=json.load(sys.stdin); assert data["status"] == "Ready"; assert data["catalog"]["provider"] == "DevelopmentMemory"' </tmp/p02-ready.json

echo "PASS: P02 Central API development vertical slice enforces 401/403 server authorization, shared catalog state, optimistic concurrency, audit evidence and readiness health."
echo "NOTE: DevelopmentMemory is non-production evidence only; SQL Server runtime/migration acceptance remains open."
