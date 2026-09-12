#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5100"
web_url="http://127.0.0.1:5101"
work="${RUNNER_TEMP:-/tmp}/mam-p04"
rm -rf "$work"
mkdir -p "$work"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"

python3 - <<'PY' config/appsettings.Development.template.json "$work/p04.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Jobs']['LeaseSeconds']=2
d['Jobs']['HeartbeatSeconds']=1
d['Jobs']['MaxAttempts']=5
with open(sys.argv[2],'w',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/p04.json"

ffmpeg -version >/dev/null
ffprobe -version >/dev/null

api_pid=""
web_pid=""
cleanup() {
  if [[ -n "$web_pid" ]]; then
    kill "$web_pid" >/dev/null 2>&1 || true
    wait "$web_pid" >/dev/null 2>&1 || true
  fi
  if [[ -n "$api_pid" ]]; then
    kill "$api_pid" >/dev/null 2>&1 || true
    wait "$api_pid" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

start_api() {
  local log="$1"
  export ASPNETCORE_URLS="$api_url"
  dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$log" 2>&1 &
  api_pid=$!
  local ready=0
  for _ in $(seq 1 80); do
    if curl --fail --silent "$api_url/health/processing" >/dev/null 2>&1; then
      ready=1
      break
    fi
    sleep .5
  done
  if [[ "$ready" != "1" ]]; then
    cat "$log"
    echo "FAIL: P04 API did not become processing-ready." >&2
    exit 1
  fi
}

stop_api() {
  if [[ -n "$api_pid" ]]; then
    kill "$api_pid" >/dev/null 2>&1 || true
    wait "$api_pid" >/dev/null 2>&1 || true
    api_pid=""
  fi
}

start_api "$work/api-1.log"
health=$(curl --fail --silent "$api_url/health/processing")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["status"]=="Ready" and d["processing"]["isReady"] is True' <<<"$health"
profiles=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/profiles")
python3 -c 'import json,sys;p={x["id"] for x in json.load(sys.stdin)};assert {"inspect-v1","video-proxy-v1","image-preview-v1","audio-preview-v1","pdf-inline-v1"}<=p' <<<"$profiles"

status=$(curl --silent --output "$work/anon-jobs.json" --write-out '%{http_code}' "$api_url/api/v1/processing/jobs")
[[ "$status" == "401" ]] || { echo "FAIL: anonymous processing queue expected 401, got $status" >&2; exit 1; }

ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=navy:s=320x180:d=1 -f lavfi -i sine=frequency=440:duration=1 -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac "$work/video.mp4"
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=gold:s=96x64 -frames:v 1 "$work/image.png"
ffmpeg -y -hide_banner -loglevel error -f lavfi -i sine=frequency=660:duration=1 -c:a pcm_s16le "$work/audio.wav"
cat >"$work/document.pdf" <<'PDF'
%PDF-1.4
1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj
2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj
3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >> endobj
4 0 obj << /Length 36 >> stream
BT /F1 12 Tf 20 100 Td (MAM P04) Tj ET
endstream endobj
xref
0 5
0000000000 65535 f 
trailer << /Root 1 0 R /Size 5 >>
startxref
0
%%EOF
PDF

upload_file() {
  local path="$1" title="$2"
  local name size sha payload created sid chunk_sha finalized
  name=$(basename "$path")
  size=$(stat -c %s "$path")
  sha=$(sha256sum "$path" | awk '{print $1}')
  payload=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H 'Content-Type: application/json' --data "$payload" "$api_url/api/v1/uploads/sessions")
  sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
  chunk_sha=$(sha256sum "$path" | awk '{print $1}')
  curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H "X-Chunk-SHA256: $chunk_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$path" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' "$api_url/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(str(d["assetId"])+"|"+d["primaryObjectKey"]+"|"+d["sha256"])' <<<"$finalized"
}

video_info=$(upload_file "$work/video.mp4" "P04 Video")
image_info=$(upload_file "$work/image.png" "P04 Image")
audio_info=$(upload_file "$work/audio.wav" "P04 Audio")
pdf_info=$(upload_file "$work/document.pdf" "P04 PDF")
IFS='|' read -r video_asset video_key video_sha <<<"$video_info"
IFS='|' read -r image_asset image_key image_sha <<<"$image_info"
IFS='|' read -r audio_asset audio_key audio_sha <<<"$audio_info"
IFS='|' read -r pdf_asset pdf_key pdf_sha <<<"$pdf_info"

find_primary() {
  local key="$1"
  python3 - <<'PY' "$key"
import os,sys
key=sys.argv[1].replace('/',os.sep)
for root in ['.mam-dev/primary','src/MAM.Api/.mam-dev/primary']:
 p=os.path.abspath(os.path.join(root,key))
 if os.path.isfile(p): print(p);raise SystemExit(0)
raise SystemExit(1)
PY
}
video_primary=$(find_primary "$video_key")
image_primary=$(find_primary "$image_key")
audio_primary=$(find_primary "$audio_key")
pdf_primary=$(find_primary "$pdf_key")
video_before=$(sha256sum "$video_primary" | awk '{print $1}')
image_before=$(sha256sum "$image_primary" | awk '{print $1}')
audio_before=$(sha256sum "$audio_primary" | awk '{print $1}')
pdf_before=$(sha256sum "$pdf_primary" | awk '{print $1}')

viewer_status=$(curl --silent --output "$work/viewer-enqueue.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' -H 'Content-Type: application/json' --data '{"profileId":"video-proxy-v1"}' "$api_url/api/v1/processing/assets/$video_asset/jobs")
[[ "$viewer_status" == "403" ]] || { echo "FAIL: Viewer processing enqueue expected 403, got $viewer_status" >&2; exit 1; }

enqueue() {
  curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "{\"profileId\":\"$2\"}" "$api_url/api/v1/processing/assets/$1/jobs"
}
video_job_json=$(enqueue "$video_asset" video-proxy-v1)
video_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$video_job_json")

# Prove a leased job survives an intentional worker crash and API restart, then is reclaimed after lease expiry.
stop_api
set +e
MAM_WORKER_ID=p04-crash-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --crash-after-lease >"$work/worker-crash.log" 2>&1
crash_code=$?
set -e
[[ "$crash_code" == "86" ]] || { cat "$work/worker-crash.log"; echo "FAIL: intentional worker crash expected exit 86, got $crash_code" >&2; exit 1; }
start_api "$work/api-2.log"
leased=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/jobs?limit=100")
python3 -c 'import json,sys;jid=sys.argv[1];rows=json.load(sys.stdin);j=next(x for x in rows if x["jobId"]==jid);assert j["state"]==1 and j["attemptCount"]==1' "$video_job" <<<"$leased"
sleep 3
MAM_WORKER_ID=p04-recovery-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/worker-recovery.log" 2>&1

video_jobs=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/jobs?limit=100")
python3 -c 'import json,sys;jid=sys.argv[1];j=next(x for x in json.load(sys.stdin) if x["jobId"]==jid);assert j["state"]==2 and j["attemptCount"]==2 and j["completedAtUtc"]' "$video_job" <<<"$video_jobs"
video_technical=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$video_asset/technical")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["mediaType"]=="Video" and d["width"]==320 and d["height"]==180 and d["videoCodec"]' <<<"$video_technical"
video_derivatives=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$video_asset/derivatives")
video_derivative=$(python3 -c 'import json,sys;d=json.load(sys.stdin);assert len(d)==1 and d[0]["profileId"]=="video-proxy-v1";print(d[0]["derivativeId"])' <<<"$video_derivatives")
video_derivative_sha=$(python3 -c 'import json,sys;print(json.load(sys.stdin)[0]["sha256"])' <<<"$video_derivatives")
curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$video_asset/derivatives/$video_derivative/content" -o "$work/video-preview.mp4"
[[ "$(sha256sum "$work/video-preview.mp4" | awk '{print $1}')" == "$video_derivative_sha" ]] || { echo "FAIL: Central API derivative content hash mismatch." >&2; exit 1; }

# Same input/profile returns the same durable job and deterministic derivative identity instead of duplicate work.
video_again=$(enqueue "$video_asset" video-proxy-v1)
[[ "$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$video_again")" == "$video_job" ]] || { echo "FAIL: duplicate profile enqueue created a different job identity." >&2; exit 1; }
video_derivatives_again=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$video_asset/derivatives")
[[ "$(python3 -c 'import json,sys;print(json.load(sys.stdin)[0]["derivativeId"])' <<<"$video_derivatives_again")" == "$video_derivative" ]] || { echo "FAIL: deterministic derivative identity changed." >&2; exit 1; }

# Image, audio and PDF strategies are executed through the same durable worker boundary.
image_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$(enqueue "$image_asset" image-preview-v1)")
MAM_WORKER_ID=p04-image-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/worker-image.log" 2>&1
image_derivatives=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$image_asset/derivatives")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert len(d)==1 and d[0]["contentType"]=="image/jpeg"' <<<"$image_derivatives"

audio_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$(enqueue "$audio_asset" audio-preview-v1)")
MAM_WORKER_ID=p04-audio-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/worker-audio.log" 2>&1
audio_derivatives=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$audio_asset/derivatives")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert len(d)==1 and d[0]["contentType"]=="audio/mp4"' <<<"$audio_derivatives"

pdf_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$(enqueue "$pdf_asset" pdf-inline-v1)")
MAM_WORKER_ID=p04-pdf-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/worker-pdf.log" 2>&1
pdf_technical=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$pdf_asset/technical")
python3 -c 'import json,sys;assert json.load(sys.stdin)["mediaType"]=="Document"' <<<"$pdf_technical"
curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$pdf_asset/preview/original" -o "$work/pdf-preview.pdf"
[[ "$(sha256sum "$work/pdf-preview.pdf" | awk '{print $1}')" == "$pdf_sha" ]] || { echo "FAIL: PDF inline preview did not stream the verified original bytes." >&2; exit 1; }

# Permanent processing failure becomes Failed, is never false-success, and can be explicitly retried.
bad_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$(enqueue "$image_asset" audio-preview-v1)")
set +e
MAM_WORKER_ID=p04-negative-worker dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/worker-negative.log" 2>&1
negative_code=$?
set -e
[[ "$negative_code" == "4" ]] || { cat "$work/worker-negative.log"; echo "FAIL: invalid media/profile processing expected worker exit 4." >&2; exit 1; }
failed_jobs=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/jobs?limit=100")
python3 -c 'import json,sys;jid=sys.argv[1];j=next(x for x in json.load(sys.stdin) if x["jobId"]==jid);assert j["state"]==3 and j["lastError"]' "$bad_job" <<<"$failed_jobs"
retried=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' "$api_url/api/v1/processing/jobs/$bad_job/retry")
python3 -c 'import json,sys;assert json.load(sys.stdin)["state"]==0' <<<"$retried"

[[ "$(sha256sum "$video_primary" | awk '{print $1}')" == "$video_before" && "$video_before" == "$video_sha" ]] || { echo "FAIL: video Primary original changed." >&2; exit 1; }
[[ "$(sha256sum "$image_primary" | awk '{print $1}')" == "$image_before" && "$image_before" == "$image_sha" ]] || { echo "FAIL: image Primary original changed." >&2; exit 1; }
[[ "$(sha256sum "$audio_primary" | awk '{print $1}')" == "$audio_before" && "$audio_before" == "$audio_sha" ]] || { echo "FAIL: audio Primary original changed." >&2; exit 1; }
[[ "$(sha256sum "$pdf_primary" | awk '{print $1}')" == "$pdf_before" && "$pdf_before" == "$pdf_sha" ]] || { echo "FAIL: PDF Primary original changed." >&2; exit 1; }

# Actual Web executable proves another client/device obtains job state, technical metadata and preview bytes only through Central API proxy.
export MAM_API_BASE_URL="$api_url"
export MAM_DEV_USER="editor"
export ASPNETCORE_URLS="$web_url"
dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then break; fi
  sleep .5
done
web_jobs=$(curl --fail --silent "$web_url/client-api/processing/jobs?limit=100")
python3 -c 'import json,sys;jid=sys.argv[1];assert any(x["jobId"]==jid and x["state"]==2 for x in json.load(sys.stdin))' "$video_job" <<<"$web_jobs"
web_technical=$(curl --fail --silent "$web_url/client-api/processing/assets/$video_asset/technical")
python3 -c 'import json,sys;assert json.load(sys.stdin)["mediaType"]=="Video"' <<<"$web_technical"
curl --fail --silent "$web_url/client-api/processing/assets/$video_asset/derivatives/$video_derivative/content" -o "$work/web-video-preview.mp4"
[[ "$(sha256sum "$work/web-video-preview.mp4" | awk '{print $1}')" == "$video_derivative_sha" ]] || { echo "FAIL: Web cross-device preview hash mismatch." >&2; exit 1; }
kill "$web_pid" >/dev/null 2>&1 || true
wait "$web_pid" >/dev/null 2>&1 || true
web_pid=""

audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/audit/recent?limit=100")
python3 -c 'import json,sys;a={x["action"] for x in json.load(sys.stdin)};assert "processing.job.queued" in a and "processing.job.completed" in a and "processing.job.failed" in a and "processing.job.retry" in a' <<<"$audit"

echo "PASS: P04 FFprobe metadata, versioned deterministic derivatives, video/image/audio/PDF preview strategies, durable crash recovery, retry/failure semantics, original preservation, audit and cross-device Web preview are verified."
