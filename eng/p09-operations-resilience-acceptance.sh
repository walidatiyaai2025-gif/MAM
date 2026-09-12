#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5232"
web_url="http://127.0.0.1:5233"
degraded_url="http://127.0.0.1:5234"
work="${RUNNER_TEMP:-/tmp}/mam-p09"
rm -rf "$work" && mkdir -p "$work"
password_key="Password"
good_connection="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_SECRET_DATABASE="$good_connection"
export MAM_SECRET_PRIMARY_STORAGE="p09-ci-secret-material-never-return-this-value"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_STORAGE_BASE_PATH="$PWD"

api_pid=""; web_pid=""; degraded_pid=""
cleanup() {
  for pid in "$web_pid" "$api_pid" "$degraded_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" >/dev/null 2>&1 || true; wait "$pid" >/dev/null 2>&1 || true; fi
  done
}
trap cleanup EXIT

start_api() {
  local url="$1" log="$2"
  export ASPNETCORE_URLS="$url"
  dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$log" 2>&1 &
  api_pid=$!
  for _ in $(seq 1 80); do
    if curl --silent "$url/health/operations" >/dev/null 2>&1; then return 0; fi
    sleep .5
  done
  cat "$log"; return 1
}

start_api "$api_url" "$work/api.log" || { echo "FAIL: P09 API did not start" >&2; exit 1; }
health_status=$(curl --silent --output "$work/health.json" --write-out '%{http_code}' "$api_url/health/operations")
[[ "$health_status" == "200" ]] || { cat "$work/health.json"; echo "FAIL: operations health expected 200, got $health_status" >&2; exit 1; }
python3 -c 'import json;d=json.load(open("'$work'/health.json"));assert d["status"]=="Ready" and d["operations"]["isReady"] is True'

anon=$(curl --silent --output "$work/anon.json" --write-out '%{http_code}' "$api_url/api/v1/operations/summary")
[[ "$anon" == "401" ]] || { echo "FAIL: anonymous operations summary expected 401, got $anon" >&2; exit 1; }
viewer=$(curl --silent --output "$work/viewer.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/operations/summary")
[[ "$viewer" == "403" ]] || { echo "FAIL: viewer operations summary expected 403, got $viewer" >&2; exit 1; }

correlation="p09-ci-correlation"
curl --fail --silent -D "$work/headers.txt" -H 'X-MAM-Dev-User: admin' -H "X-Correlation-ID: $correlation" "$api_url/api/v1/operations/summary" >"$work/summary-before.json"
grep -qi "^X-Correlation-ID: ${correlation}" "$work/headers.txt" || { cat "$work/headers.txt"; echo "FAIL: supplied safe correlation id was not returned" >&2; exit 1; }
python3 - "$work/summary-before.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1]))
keys=['assets','originals','originalBytes','uploadReceiving','uploadFailed','processingQueued','processingLeased','processingFailed','backupQueued','backupLeased','backupFailed','protected','protectionPending','protectionFailed','protectionMismatch']
assert all(isinstance(d[k],int) and d[k]>=0 for k in keys)
assert d['protected'] <= d['originals']
PY

throughput=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/throughput?windowHours=24")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["windowHours"]==24 and d["completedSessions"]>=0 and d["completedBytes"]>=0 and d["averageBytesPerSecond"]>=0' <<<"$throughput"
bad_window=$(curl --silent --output "$work/bad-window.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/throughput?windowHours=0")
[[ "$bad_window" == "400" ]] || { echo "FAIL: invalid throughput window expected 400, got $bad_window" >&2; exit 1; }

queues=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/queues")
printf '%s' "$queues" >"$work/queues.json"
python3 - "$work/queues.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1])); items={x['queue']:x for x in d['queues']}
assert {'Processing','Backup','CaptureUploadHandoff'} <= set(items)
for q in items.values():
    for k in ('pending','leased','failed','staleLeases'): assert q[k]>=0
PY

integrity=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/integrity")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["originals"]>=d["protected"]>=0 and d["originalBytes"]>=d["protectedBytes"]>=0 and d["pending"]>=0 and d["failed"]>=0 and d["mismatch"]>=0' <<<"$integrity"
storage=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/storage")
printf '%s' "$storage" >"$work/storage.json"
python3 - "$work/storage.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1])); assert d['authoritativeOriginalBytes']>=d['verifiedProtectedBytes']>=0
assert d['originalCount']>=d['protectedCount']>=0
assert '/' not in d['primaryTargetId'] and '\\' not in d['primaryTargetId']
assert '/' not in d['backupTargetId'] and '\\' not in d['backupTargetId']
PY

deps=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/dependencies")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert len(d["items"])>=7;assert all(x["status"] in ("Ready","Degraded") for x in d["items"])' <<<"$deps"

diag=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/diagnostics")
printf '%s' "$diag" >"$work/diagnostics.json"
[[ "$diag" != *"${MAM_SQL_TEST_PASSWORD}"* ]] || { echo "FAIL: SQL secret leaked in diagnostics" >&2; exit 1; }
[[ "$diag" != *"p09-ci-secret-material-never-return-this-value"* ]] || { echo "FAIL: storage secret leaked in diagnostics" >&2; exit 1; }
python3 - "$work/diagnostics.json" <<'PY'
import json,sys,re
d=json.load(open(sys.argv[1])); assert d['phase']=='P09' and len(d['correlationId'])>0 and len(d['redactionPolicy'])>=3
forbidden={'password','passwd','clientsecret','apikey','token','accesstoken','refreshtoken','connectionstring','privatekey','secretref','credentialref','root'}
def walk(v):
    if isinstance(v,dict):
        assert not ({str(k).lower() for k in v} & forbidden)
        for x in v.values(): walk(x)
    elif isinstance(v,list):
        for x in v: walk(x)
walk(d)
PY

# Intentional API restart: authoritative report totals must survive unchanged.
kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; api_pid=""
start_api "$api_url" "$work/api-restart.log" || { echo "FAIL: P09 API did not recover after restart" >&2; exit 1; }
curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/operations/summary" >"$work/summary-after.json"
python3 - "$work/summary-before.json" "$work/summary-after.json" <<'PY'
import json,sys
a=json.load(open(sys.argv[1])); b=json.load(open(sys.argv[2]))
for k in a:
    if k!='generatedAtUtc': assert a[k]==b[k], (k,a[k],b[k])
PY

# Inject SQL dependency failure in a separate API process; health must fail closed, then the healthy instance remains ready.
export MAM_SECRET_DATABASE="Server=127.0.0.1,1;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=not-a-real-secret;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1"
export ASPNETCORE_URLS="$degraded_url"
dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api-degraded.log" 2>&1 &
degraded_pid=$!
for _ in $(seq 1 60); do
  degraded_status=$(curl --silent --output "$work/degraded.json" --write-out '%{http_code}' "$degraded_url/health/operations" || true)
  [[ "$degraded_status" == "503" ]] && break
  sleep .5
done
[[ "${degraded_status:-}" == "503" ]] || { cat "$work/api-degraded.log"; echo "FAIL: injected SQL failure was not reported as degraded" >&2; exit 1; }
python3 -c 'import json;d=json.load(open("'$work'/degraded.json"));assert d["status"]=="Degraded" and d["operations"]["isReady"] is False' || exit 1
kill "$degraded_pid" >/dev/null 2>&1 || true; wait "$degraded_pid" >/dev/null 2>&1 || true; degraded_pid=""
export MAM_SECRET_DATABASE="$good_connection"
healthy_again=$(curl --silent --output /dev/null --write-out '%{http_code}' "$api_url/health/operations")
[[ "$healthy_again" == "200" ]] || { echo "FAIL: healthy API did not remain ready after isolated failure injection" >&2; exit 1; }

export MAM_API_BASE_URL="$api_url"
export MAM_DEV_USER=admin
export ASPNETCORE_URLS="$web_url"
dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep .5
done
[[ "${web_ready:-0}" == "1" ]] || { cat "$work/web.log"; echo "FAIL: P09 Web did not become ready" >&2; exit 1; }
web_summary=$(curl --fail --silent "$web_url/client-api/operations/summary")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["assets"]>=0 and d["originals"]>=0' <<<"$web_summary"
web_diag=$(curl --fail --silent "$web_url/client-api/operations/diagnostics")
[[ "$web_diag" != *"${MAM_SQL_TEST_PASSWORD}"* && "$web_diag" != *"p09-ci-secret-material-never-return-this-value"* ]] || { echo "FAIL: Web diagnostics proxy leaked secret material" >&2; exit 1; }
html=$(curl --fail --silent "$web_url/")
[[ "$html" == *"P09 · NON-PRODUCTION"* && "$html" == *"p09-operations.js"* ]] || { echo "FAIL: P09 Web operations shell is not activated" >&2; exit 1; }

echo "P09 operations, monitoring and restart resilience acceptance: PASS"
