#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5096"
web_url="http://127.0.0.1:5097"
work="${RUNNER_TEMP:-/tmp}/mam-p03"
mkdir -p "$work"
rm -rf .mam-dev/primary
mkdir -p .mam-dev/primary
password_key="Password"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=true
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"

api_pid=""
web_pid=""
cleanup() {
  if [[ -n "$web_pid" ]]; then kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; fi
  if [[ -n "$api_pid" ]]; then kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

start_api() {
  local log="$1"
  export ASPNETCORE_URLS="$api_url"
  dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$log" 2>&1 &
  api_pid=$!
  local ready=0
  for _ in $(seq 1 80); do
    if curl --fail --silent "$api_url/health/ready" >/dev/null 2>&1; then ready=1; break; fi
    sleep 0.5
  done
  if [[ "$ready" != "1" ]]; then cat "$log"; echo "FAIL: P03 API did not become ready." >&2; exit 1; fi
}

stop_api() {
  if [[ -n "$api_pid" ]]; then kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; api_pid=""; fi
}

start_api "$work/api-1.log"

status=$(curl --silent --output "$work/storage-health.json" --write-out '%{http_code}' "$api_url/health/storage")
[[ "$status" == "200" ]] || { cat "$work/storage-health.json"; echo "FAIL: Primary Storage health expected 200, got $status" >&2; exit 1; }
python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); assert d["status"]=="Ready" and d["upload"]["isReady"] is True' "$work/storage-health.json"

status=$(curl --silent --output "$work/anon.json" --write-out '%{http_code}' -H 'Content-Type: application/json' --data '{}' "$api_url/api/v1/uploads/sessions")
[[ "$status" == "401" ]] || { cat "$work/anon.json"; echo "FAIL: anonymous upload session expected 401, got $status" >&2; exit 1; }
status=$(curl --silent --output "$work/viewer.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' -H 'Content-Type: application/json' --data '{}' "$api_url/api/v1/uploads/sessions")
[[ "$status" == "403" ]] || { cat "$work/viewer.json"; echo "FAIL: Viewer upload session expected 403, got $status" >&2; exit 1; }

python3 - <<'PY' "$work/windows-large.mp4"
import sys
path=sys.argv[1]
size=20*1024*1024
block=bytes((i*17+23)%256 for i in range(1024*1024))
with open(path,'wb') as f:
    for _ in range(size//len(block)): f.write(block)
PY
large_sha=$(sha256sum "$work/windows-large.mp4" | awk '{print $1}')
large_size=$(stat -c %s "$work/windows-large.mp4")
create_payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"P03 Windows Resumable Asset","originalFileName":"windows-large.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$large_size" "$large_sha")
created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H 'Content-Type: application/json' --data "$create_payload" "$api_url/api/v1/uploads/sessions")
session_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
asset_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["assetId"])' <<<"$created")
chunk_size=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["chunkSizeBytes"])' <<<"$created")
[[ "$chunk_size" == "16777216" ]] || { echo "FAIL: expected 16MiB chunk size, got $chunk_size" >&2; exit 1; }
head -c "$chunk_size" "$work/windows-large.mp4" > "$work/chunk-1.bin"
chunk_sha=$(sha256sum "$work/chunk-1.bin" | awk '{print $1}')
first=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H "X-Chunk-SHA256: $chunk_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/chunk-1.bin" "$api_url/api/v1/uploads/sessions/$session_id/chunks?offset=0")
first_received=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["receivedLength"])' <<<"$first")
[[ "$first_received" == "$chunk_size" ]] || { echo "FAIL: first acknowledged offset mismatch." >&2; exit 1; }

# Simulate a server/API interruption after an acknowledged chunk, then recover the same SQL session + server staging file.
stop_api
start_api "$work/api-2.log"
resumed=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' "$api_url/api/v1/uploads/sessions/$session_id")
resume_offset=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["receivedLength"])' <<<"$resumed")
[[ "$resume_offset" == "$chunk_size" ]] || { echo "FAIL: resumed offset was $resume_offset instead of $chunk_size" >&2; exit 1; }
tail -c +$((chunk_size+1)) "$work/windows-large.mp4" > "$work/chunk-2.bin"
chunk2_sha=$(sha256sum "$work/chunk-2.bin" | awk '{print $1}')
second=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H "X-Chunk-SHA256: $chunk2_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/chunk-2.bin" "$api_url/api/v1/uploads/sessions/$session_id/chunks?offset=$resume_offset")
second_received=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["receivedLength"])' <<<"$second")
[[ "$second_received" == "$large_size" ]] || { echo "FAIL: final received length mismatch." >&2; exit 1; }
finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' "$api_url/api/v1/uploads/sessions/$session_id/finalize")
final_sha=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["sha256"])' <<<"$finalized")
object_key=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["primaryObjectKey"])' <<<"$finalized")
[[ "$final_sha" == "$large_sha" ]] || { echo "FAIL: finalized SHA mismatch." >&2; exit 1; }
# Development roots are intentionally relative; dotnet run may preserve either the repo or project working directory.
# Prove the exact server-generated object key exists under the configured relative Primary target without assuming CWD.
primary_path=$(find "$PWD" -type f -path "*/.mam-dev/primary/$object_key" -print -quit)
[[ -n "$primary_path" && -f "$primary_path" ]] || { echo "FAIL: verified Primary original was not materialized server-side for object key $object_key." >&2; find "$PWD" -maxdepth 8 -type f -path '*/.mam-dev/primary/*' -print >&2 || true; exit 1; }
[[ "$(stat -c %s "$primary_path")" == "$large_size" ]] || { echo "FAIL: Primary original size mismatch." >&2; exit 1; }
[[ "$(sha256sum "$primary_path" | awk '{print $1}')" == "$large_sha" ]] || { echo "FAIL: Primary original SHA mismatch." >&2; exit 1; }

catalog=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: WebPortal' "$api_url/api/v1/catalog/assets")
python3 -c 'import json,sys; aid=sys.argv[1]; data=json.load(sys.stdin); assert any(str(x["id"])==aid and x["title"]=="P03 Windows Resumable Asset" for x in data)' "$asset_id" <<<"$catalog"

# Unsafe filename/path input fails closed before any staging or Primary write.
unsafe_payload=$(python3 -c 'import json; print(json.dumps({"title":"Unsafe","originalFileName":"../escape.mp4","expectedLength":10,"expectedSha256":"0"*64}))')
status=$(curl --silent --output "$work/unsafe.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$unsafe_payload" "$api_url/api/v1/uploads/sessions")
[[ "$status" == "400" ]] || { cat "$work/unsafe.json"; echo "FAIL: traversal filename expected 400, got $status" >&2; exit 1; }

# Wrong per-chunk hash fails without advancing the durable offset.
printf 'chunk-hash-negative' > "$work/wrong.mp4"
wrong_sha=$(sha256sum "$work/wrong.mp4" | awk '{print $1}')
wrong_size=$(stat -c %s "$work/wrong.mp4")
wrong_payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"Wrong hash negative","originalFileName":"wrong.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$wrong_size" "$wrong_sha")
wrong_created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$wrong_payload" "$api_url/api/v1/uploads/sessions")
wrong_session=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$wrong_created")
status=$(curl --silent --output "$work/wrong-chunk.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: editor' -H "X-Chunk-SHA256: $(printf '0%.0s' {1..64})" -H 'Content-Type: application/octet-stream' --data-binary @"$work/wrong.mp4" "$api_url/api/v1/uploads/sessions/$wrong_session/chunks?offset=0")
[[ "$status" == "422" ]] || { cat "$work/wrong-chunk.json"; echo "FAIL: wrong chunk hash expected 422, got $status" >&2; exit 1; }
wrong_state=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' "$api_url/api/v1/uploads/sessions/$wrong_session")
python3 -c 'import json,sys; assert json.load(sys.stdin)["receivedLength"]==0' <<<"$wrong_state"

# Duplicate policy is deterministic: same authoritative SHA returns 409 and existing asset identity.
dup_payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"Duplicate","originalFileName":"duplicate.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$large_size" "$large_sha")
status=$(curl --silent --output "$work/duplicate.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$dup_payload" "$api_url/api/v1/uploads/sessions")
[[ "$status" == "409" ]] || { cat "$work/duplicate.json"; echo "FAIL: duplicate expected 409, got $status" >&2; exit 1; }
python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); assert d["error"]=="duplicate_detected" and str(d["existingAssetId"])==sys.argv[2]' "$work/duplicate.json" "$asset_id"

# Unknown file types are accepted only into quarantine and cannot be promoted to Primary.
printf 'quarantine-me' > "$work/quarantine.exe"
q_sha=$(sha256sum "$work/quarantine.exe" | awk '{print $1}')
q_size=$(stat -c %s "$work/quarantine.exe")
q_payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"Quarantine Negative","originalFileName":"quarantine.exe","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$q_size" "$q_sha")
q_created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$q_payload" "$api_url/api/v1/uploads/sessions")
q_session=$(python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["isQuarantined"] is True; print(d["session"]["sessionId"])' <<<"$q_created")
q_chunk_sha=$(sha256sum "$work/quarantine.exe" | awk '{print $1}')
curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H "X-Chunk-SHA256: $q_chunk_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/quarantine.exe" "$api_url/api/v1/uploads/sessions/$q_session/chunks?offset=0" >/dev/null
status=$(curl --silent --output "$work/quarantine-finalize.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: editor' "$api_url/api/v1/uploads/sessions/$q_session/finalize")
[[ "$status" == "422" ]] || { cat "$work/quarantine-finalize.json"; echo "FAIL: quarantine finalize expected 422, got $status" >&2; exit 1; }

# Actual MAM.Web proxy proves the Web client uses the same Central API without Primary credentials.
export MAM_API_BASE_URL="$api_url"
export MAM_DEV_USER="editor"
export ASPNETCORE_URLS="$web_url"
dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
web_ready=0
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep 0.5
done
[[ "$web_ready" == "1" ]] || { cat "$work/web.log"; echo "FAIL: MAM.Web did not become ready." >&2; exit 1; }
web_catalog=$(curl --fail --silent "$web_url/client-api/catalog/assets")
python3 -c 'import json,sys; aid=sys.argv[1]; data=json.load(sys.stdin); assert any(str(x["id"])==aid for x in data)' "$asset_id" <<<"$web_catalog"

python3 - <<'PY' "$work/web-small.mp4"
import sys
with open(sys.argv[1],'wb') as f: f.write((b'MAM-P03-WEB-'*65536)[:786432])
PY
web_sha=$(sha256sum "$work/web-small.mp4" | awk '{print $1}')
web_size=$(stat -c %s "$work/web-small.mp4")
web_payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"P03 Web Proxy Asset","originalFileName":"web-small.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$web_size" "$web_sha")
web_created=$(curl --fail --silent -H 'Content-Type: application/json' --data "$web_payload" "$web_url/client-api/uploads/sessions")
web_session=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$web_created")
web_asset=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["assetId"])' <<<"$web_created")
web_chunk_sha=$(sha256sum "$work/web-small.mp4" | awk '{print $1}')
curl --fail --silent -X PUT -H "X-Chunk-SHA256: $web_chunk_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/web-small.mp4" "$web_url/client-api/uploads/sessions/$web_session/chunks?offset=0" >/dev/null
curl --fail --silent -X POST "$web_url/client-api/uploads/sessions/$web_session/finalize" >/dev/null
api_catalog=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/catalog/assets")
python3 -c 'import json,sys; aid=sys.argv[1]; data=json.load(sys.stdin); assert any(str(x["id"])==aid and x["title"]=="P03 Web Proxy Asset" for x in data)' "$web_asset" <<<"$api_catalog"

kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; web_pid=""

# Inject a Primary Storage outage and require explicit degraded health rather than false readiness.
stop_api
python3 - <<'PY' config/appsettings.Development.template.json "$work/degraded.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f: d=json.load(f)
d['Storage']['Primary']['Root']='/proc/mam-p03-unwritable'
with open(sys.argv[2],'w',encoding='utf-8') as f: json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/degraded.json"
export MAM_APPLY_MIGRATIONS=false
start_api "$work/api-degraded.log"
status=$(curl --silent --output "$work/degraded-health.json" --write-out '%{http_code}' "$api_url/health/storage")
[[ "$status" == "503" ]] || { cat "$work/degraded-health.json"; echo "FAIL: injected Primary outage expected 503, got $status" >&2; exit 1; }
python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); assert d["status"]=="Degraded" and d["upload"]["isReady"] is False' "$work/degraded-health.json"

echo "PASS: P03 durable upload resumed after API interruption, verified server-side size/SHA-256 before Primary promotion, enforced path/hash/duplicate/quarantine policy, exposed degraded storage health, and synchronized Windows/Web catalog state."
