#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5120"
web_url="http://127.0.0.1:5121"
work="${RUNNER_TEMP:-/tmp}/mam-p06"
rm -rf "$work" && mkdir -p "$work/backup"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"

python3 - <<'PY' config/appsettings.Development.template.json "$work/p06.json" "$work/backup"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Storage']['Backup']['Root']=sys.argv[3]
d['Storage']['Backup']['MinimumFreeGB']=0
d['Storage']['Backup']['RetryBackoffSeconds']=1
d['Storage']['Backup']['MaxRetryCount']=4
d['Jobs']['LeaseSeconds']=15
d['Jobs']['HeartbeatSeconds']=5
with open(sys.argv[2],'w',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/p06.json"

api_pid=""; web_pid=""
cleanup(){
  [[ -z "$web_pid" ]] || { kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; }
  [[ -z "$api_pid" ]] || { kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; }
  chmod -R u+rwx "$work/backup" >/dev/null 2>&1 || true
}
trap cleanup EXIT

start_api(){
  ASPNETCORE_URLS="$api_url" dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api.log" 2>&1 & api_pid=$!
  for _ in $(seq 1 80); do curl --fail --silent "$api_url/health/protection" >/dev/null 2>&1 && return; sleep .5; done
  cat "$work/api.log"; echo 'FAIL: P06 API did not become protection-ready.' >&2; exit 1
}
start_web(){
  MAM_API_BASE_URL="$api_url" MAM_DEV_USER=admin ASPNETCORE_URLS="$web_url" dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 & web_pid=$!
  for _ in $(seq 1 80); do curl --fail --silent "$web_url/client-api/status" >/dev/null 2>&1 && return; sleep .5; done
  cat "$work/web.log"; echo 'FAIL: P06 Web proxy did not become ready.' >&2; exit 1
}

upload_file(){
  local path="$1" title="$2" name size sha payload created sid chunk finalized
  name=$(basename "$path"); size=$(stat -c %s "$path"); sha=$(sha256sum "$path"|awk '{print $1}')
  payload=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$payload" "$api_url/api/v1/uploads/sessions")
  sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
  chunk=$(sha256sum "$path"|awk '{print $1}')
  curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H "X-Chunk-SHA256: $chunk" -H 'Content-Type: application/octet-stream' --data-binary @"$path" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' "$api_url/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(str(d["assetId"])+"|"+d["primaryObjectKey"]+"|"+d["sha256"]+"|"+str(d["length"]))' <<<"$finalized"
}

get_state(){ curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/protection/assets/$1"; }
queue(){ curl --fail --silent -X POST -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/protection/queue" >/dev/null; }
run_backup_once(){ MAM_WORKER_ID="$1" dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --backup-only --once >"$work/$1.log" 2>&1; }

start_api
start_web

health=$(curl --fail --silent "$api_url/health/protection")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["status"]=="Ready" and d["protection"]["isReady"] is True and d["protection"]["primaryTargetId"]!=d["protection"]["backupTargetId"]' <<<"$health"

anon=$(curl --silent -o /dev/null -w '%{http_code}' "$api_url/api/v1/protection/summary")
[[ "$anon" == 401 ]] || { echo "FAIL: anonymous protection summary expected 401, got $anon" >&2; exit 1; }
viewer_queue=$(curl --silent -o /dev/null -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/protection/queue")
[[ "$viewer_queue" == 403 ]] || { echo "FAIL: viewer protection queue expected 403, got $viewer_queue" >&2; exit 1; }

printf 'P06 verified backup payload %s\n' "$(date +%s%N)" >"$work/asset-a.mp4"
a_info=$(upload_file "$work/asset-a.mp4" 'P06 Protected Asset')
IFS='|' read -r a_id a_key a_sha a_len <<<"$a_info"
a_primary="$PWD/.mam-dev/primary/${a_key//\//\/}"
a_primary_sha_before=$(sha256sum "$a_primary"|awk '{print $1}')
queue
pending=$(get_state "$a_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==0 and d["verifiedAtUtc"] is None' <<<"$pending"
run_backup_once p06-copy-worker
protected=$(get_state "$a_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==1 and d["verifiedAtUtc"] and d["expectedLength"]==int(sys.argv[1])' "$a_len" <<<"$protected"
a_backup_key=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["backupObjectKey"])' <<<"$protected")
a_backup="$work/backup/${a_backup_key//\//\/}"
[[ -f "$a_backup" ]] || { echo 'FAIL: verified Backup copy missing.' >&2; exit 1; }
[[ "$(sha256sum "$a_backup"|awk '{print $1}')" == "$a_sha" ]] || { echo 'FAIL: Backup SHA-256 differs from authoritative Primary.' >&2; exit 1; }
[[ "$(stat -c %s "$a_backup")" == "$a_len" ]] || { echo 'FAIL: Backup length differs from Primary.' >&2; exit 1; }
[[ "$(sha256sum "$a_primary"|awk '{print $1}')" == "$a_primary_sha_before" ]] || { echo 'FAIL: Primary changed during Backup copy.' >&2; exit 1; }

# Corrupt an already Protected backup, force periodic integrity recheck, require visible Mismatch first.
printf 'CORRUPTION' >>"$a_backup"
curl --fail --silent -X POST -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/protection/integrity/recheck?olderThanHours=0" >/dev/null
set +e
run_backup_once p06-integrity-worker
integrity_code=$?
set -e
[[ "$integrity_code" == 5 ]] || { cat "$work/p06-integrity-worker.log"; echo "FAIL: mismatched backup worker expected fail-closed exit 5, got $integrity_code" >&2; exit 1; }
mismatch=$(get_state "$a_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==3 and d["verifiedAtUtc"] is None and d["lastError"]' <<<"$mismatch"
[[ "$(sha256sum "$a_primary"|awk '{print $1}')" == "$a_primary_sha_before" ]] || { echo 'FAIL: Primary changed during corruption detection.' >&2; exit 1; }

# Retry repairs only after mismatch was persisted and independently visible.
sleep 2
run_backup_once p06-repair-worker
repaired=$(get_state "$a_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==1 and d["verifiedAtUtc"] and d["lastError"] is None' <<<"$repaired"
[[ "$(sha256sum "$a_backup"|awk '{print $1}')" == "$a_sha" ]] || { echo 'FAIL: mismatch repair did not restore verified Backup bytes.' >&2; exit 1; }

# Backup outage: make target non-writable. Job must become BackupFailed while Primary is untouched.
printf 'P06 outage payload %s\n' "$(date +%s%N)" >"$work/asset-b.mov"
b_info=$(upload_file "$work/asset-b.mov" 'P06 Backup Outage Asset')
IFS='|' read -r b_id b_key b_sha b_len <<<"$b_info"
b_primary="$PWD/.mam-dev/primary/${b_key//\//\/}"
b_primary_before=$(sha256sum "$b_primary"|awk '{print $1}')
queue
chmod -R a-w "$work/backup"
set +e
run_backup_once p06-outage-worker
outage_code=$?
set -e
chmod -R u+w "$work/backup"
[[ "$outage_code" == 5 ]] || { cat "$work/p06-outage-worker.log"; echo "FAIL: outage worker expected exit 5, got $outage_code" >&2; exit 1; }
outage=$(get_state "$b_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==2 and d["verifiedAtUtc"] is None and d["lastError"]' <<<"$outage"
[[ "$(sha256sum "$b_primary"|awk '{print $1}')" == "$b_primary_before" ]] || { echo 'FAIL: Primary changed during Backup outage.' >&2; exit 1; }
sleep 2
run_backup_once p06-outage-recovery
python3 -c 'import json,sys;assert json.load(sys.stdin)["state"]==1' <<<"$(get_state "$b_id")"

# Crash after backup lease, wait for stale lease expiry, then recover same durable job.
printf 'P06 stale lease payload %s\n' "$(date +%s%N)" >"$work/asset-c.wav"
c_info=$(upload_file "$work/asset-c.wav" 'P06 Stale Lease Asset')
IFS='|' read -r c_id c_key c_sha c_len <<<"$c_info"
queue
set +e
MAM_WORKER_ID=p06-crash-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --backup-only --crash-after-backup-lease >"$work/p06-crash-worker.log" 2>&1
crash_code=$?
set -e
[[ "$crash_code" == 86 ]] || { cat "$work/p06-crash-worker.log"; echo "FAIL: backup crash expected exit 86, got $crash_code" >&2; exit 1; }
sleep 16
run_backup_once p06-stale-recovery
c_state=$(get_state "$c_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["state"]==1 and d["verifiedAtUtc"]' <<<"$c_state"

summary=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/protection/summary")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["protected"]>=3 and d["mismatch"]==0 and d["failed"]==0' <<<"$summary"
web_summary=$(curl --fail --silent "$web_url/client-api/protection/summary")
python3 -c 'import json,sys;assert json.load(sys.stdin)["protected"]>=3' <<<"$web_summary"
web_status=$(curl --fail --silent "$web_url/client-api/status")
python3 -c 'import json,sys;assert json.load(sys.stdin)["protectionConfigured"] is True' <<<"$web_status"
audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/audit/recent?limit=200")
python3 -c 'import json,sys;actions={x["action"] for x in json.load(sys.stdin)};assert "backup.protected" in actions and "backup.mismatch" in actions and "backup.failed" in actions and "backup.repair" in actions' <<<"$audit"

# No storage roots or credentials may leak through protection health/status responses.
if printf '%s\n%s\n' "$health" "$protected" | grep -Fq "$work/backup"; then echo 'FAIL: Backup filesystem root leaked to client response.' >&2; exit 1; fi
if printf '%s\n%s\n' "$health" "$protected" | grep -qiE 'Password=|CredentialRef|ConnectionString'; then echo 'FAIL: sensitive storage/database material leaked to client response.' >&2; exit 1; fi

echo 'PASS: P06 independently copied verified Primary originals to separate Backup, blocked false Protected state, detected/repaired corruption, persisted Backup outage failure without modifying Primary, recovered stale leases, exposed governed API/Web state, and emitted audit evidence.'
