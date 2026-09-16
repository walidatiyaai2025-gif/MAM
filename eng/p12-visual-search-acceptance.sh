#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5127"
work="${RUNNER_TEMP:-/tmp}/mam-p12-visual-search"
rm -rf "$work" .mam-dev src/MAM.Api/.mam-dev
mkdir -p "$work"
password_key="Pass""word"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14335;Initial Catalog=MamP12VisualCi;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"

python3 - <<'PY' config/appsettings.Development.template.json "$work/config.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Jobs']['LeaseSeconds']=30
d['Jobs']['HeartbeatSeconds']=5
d['Jobs']['MaxAttempts']=5
with open(sys.argv[2],'w',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/config.json"

cat >"$work/fake-whisper" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
out=""
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "-of" ]]; then shift; out="${1:-}"; fi
  shift || true
done
[[ -n "$out" ]]
cat >"${out}.vtt" <<'VTT'
WEBVTT

00:00:00.000 --> 00:00:01.400
VISUAL SEGMENT ALPHA

00:00:01.400 --> 00:00:02.800
VISUAL SEGMENT BETA
VTT
SH
chmod +x "$work/fake-whisper"
printf 'fake-model-for-visual-ci' >"$work/fake-model.bin"
export MAM_WHISPER_PATH="$work/fake-whisper"
export MAM_WHISPER_MODEL_PATH="$work/fake-model.bin"
export MAM_WHISPER_LANGUAGE=en

api_pid=""
stop_api(){
  if [[ -n "$api_pid" ]]; then kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; api_pid=""; fi
}
cleanup(){ stop_api; }
trap cleanup EXIT

start_api(){
  local label="$1"
  export ASPNETCORE_URLS="$api_url"
  dotnet run --project src/MAM.Api/MAM.Api.csproj -c Release --no-build >"$work/api-$label.log" 2>&1 & api_pid=$!
  for _ in $(seq 1 120); do
    if curl -fsS "$api_url/health/live" >/dev/null 2>&1; then return 0; fi
    sleep .25
  done
  cat "$work/api-$label.log" >&2
  return 1
}

req(){
  local user="$1" method="$2" path="$3" body="${4:-}"
  if [[ -n "$body" ]]; then
    curl -fsS -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12VisualAcceptance' -H 'Content-Type: application/json' --data "$body" "$api_url$path"
  else
    curl -fsS -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12VisualAcceptance' "$api_url$path"
  fi
}

upload(){
  local file="$1" title="$2" name size sha body session sid final
  name=$(basename "$file"); size=$(stat -c %s "$file"); sha=$(sha256sum "$file" | awk '{print $1}')
  body=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  session=$(req editor POST /api/v1/uploads/sessions "$body")
  sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$session")
  curl -fsS -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: P12VisualAcceptance' -H "X-Chunk-SHA256: $sha" -H 'Content-Type: application/octet-stream' --data-binary @"$file" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  final=$(req editor POST "/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(d["assetId"]+"|"+d["sha256"])' <<<"$final"
}

queue(){ req editor POST "/api/v1/processing/assets/$1/jobs" "{\"profileId\":\"$2\"}"; }
worker(){
  local name="$1"
  MAM_WORKER_ID="p12-visual-$name" dotnet run --project src/MAM.Worker/MAM.Worker.csproj -c Release --no-build -- --processing-only --once >"$work/worker-$name.log" 2>&1 || { cat "$work/worker-$name.log" >&2; return 1; }
}
assert_job(){
  local id="$1" jobs
  jobs=$(req viewer GET '/api/v1/processing/jobs?limit=200')
  python3 -c 'import json,sys;j=next(x for x in json.load(sys.stdin) if x["jobId"]==sys.argv[1]);assert j["state"]==2 and j["completedAtUtc"] and not j["lastError"],j' "$id" <<<"$jobs"
}
image_search(){
  local user="$1" file="$2"
  curl -fsS -X POST -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12VisualAcceptance' -H "Content-Type: $(file -b --mime-type "$file")" --data-binary @"$file" "$api_url/api/v1/discovery/image-search?limit=30"
}

# Deterministic query fixtures and a video with stable representative frames.
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=white:s=320x180 -frames:v 1 "$work/white.png"
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=black:s=320x180 -frames:v 1 "$work/black.png"
ffmpeg -y -hide_banner -loglevel error -f lavfi -i testsrc=size=320x180:rate=25:duration=3 -f lavfi -i sine=frequency=660:duration=3 -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac "$work/video.mp4"
ffmpeg -y -hide_banner -loglevel error -ss 0.7 -i "$work/video.mp4" -frames:v 1 "$work/video-query.jpg"

start_api enabled
curl -fsS "$api_url/health/visual-search" | python3 -c 'import json,sys;d=json.load(sys.stdin);v=d["visual"];assert d["status"]=="Ready" and v["isReady"] and v["dimensions"]==768 and v["modelVersion"]==1'

# Empty index is a truthful no-match response.
image_search viewer "$work/white.png" | python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["items"]==[] and d["dimensions"]==768'

# Input validation must fail closed.
code=$(curl -sS -o "$work/invalid-type.json" -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12VisualAcceptance' -H 'Content-Type: text/plain' --data 'not-an-image' "$api_url/api/v1/discovery/image-search"); [[ "$code" == 415 ]]
code=$(curl -sS -o "$work/empty-image.json" -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12VisualAcceptance' -H 'Content-Type: image/png' --data-binary '' "$api_url/api/v1/discovery/image-search"); [[ "$code" == 400 ]]

# Asset-level image indexing and exact-image similarity.
IFS='|' read -r image_asset image_sha <<<"$(upload "$work/white.png" 'Visual White Image')"
job=$(queue "$image_asset" visual-index-v1); image_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$job")
worker image-index
assert_job "$image_job"
IMAGE_ASSET="$image_asset" image_search viewer "$work/white.png" | IMAGE_ASSET="$image_asset" python3 -c 'import json,sys,os;d=json.load(sys.stdin);h=next(x for x in d["items"] if x["assetId"]==os.environ["IMAGE_ASSET"]);assert h["segmentId"] is None and h["mediaKind"]=="Image" and h["score"]>0.99 and d["provider"]=="LocalImageGrid"'

# Production reindex is idempotent for the same profile/model/source.
r1=$(req editor POST "/api/v1/discovery/assets/$image_asset/visual-reindex")
r2=$(req editor POST "/api/v1/discovery/assets/$image_asset/visual-reindex")
python3 -c 'import json,sys;a=json.loads(sys.argv[1]);b=json.loads(sys.argv[2]);assert a["jobId"]==b["jobId"] and a["state"]==2 and b["state"]==2' "$r1" "$r2"

# Timestamped video transcript automatically queues lower-priority visual segment work.
IFS='|' read -r video_asset video_sha <<<"$(upload "$work/video.mp4" 'Visual Transcript Video')"
job=$(queue "$video_asset" transcript-text-v1); transcript_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$job")
worker video-transcript
assert_job "$transcript_job"
worker video-visual
segments=$(req viewer GET "/api/v1/discovery/assets/$video_asset/visual-segments?sourceKind=transcript")
VIDEO_ASSET="$video_asset" python3 -c 'import json,sys,os;s=json.loads(sys.argv[1]);assert len(s)==2;assert all(x["assetId"]==os.environ["VIDEO_ASSET"] and x["visualState"]=="Ready" and x["hasThumbnail"] for x in s);assert s[0]["startMs"]==0 and s[0]["endMs"]==1400' "$segments"
segment_id=$(python3 -c 'import json,sys;print(json.loads(sys.argv[1])[0]["segmentId"])' "$segments")
headers=$(curl -fsS -D - -o "$work/segment.jpg" -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12VisualAcceptance' "$api_url/api/v1/discovery/assets/$video_asset/visual-segments/$segment_id/thumbnail")
grep -qi '^content-type: image/jpeg' <<<"$headers"
[[ -s "$work/segment.jpg" ]]
VIDEO_ASSET="$video_asset" image_search viewer "$work/video-query.jpg" | VIDEO_ASSET="$video_asset" python3 -c 'import json,sys,os;d=json.load(sys.stdin);hits=[x for x in d["items"] if x["assetId"]==os.environ["VIDEO_ASSET"] and x["segmentId"]];assert hits and max(x["score"] for x in hits)>0.85'

# Server-side media-view permission filtering: viewer loses image visibility.
req admin PUT /api/v1/discovery/media-permissions '{"roleName":"Viewer","mediaKind":"Image","canView":false,"canUpload":false,"canEdit":false,"canProcess":false,"canDownload":false}' >/dev/null
IMAGE_ASSET="$image_asset" image_search viewer "$work/white.png" | IMAGE_ASSET="$image_asset" python3 -c 'import json,sys,os;assert all(x["assetId"]!=os.environ["IMAGE_ASSET"] for x in json.load(sys.stdin)["items"])'
req admin PUT /api/v1/discovery/media-permissions '{"roleName":"Viewer","mediaKind":"Image","canView":true,"canUpload":false,"canEdit":false,"canProcess":false,"canDownload":false}' >/dev/null

# Asset deletion must remove DB visual rows and the physical segment thumbnail derivative.
asset_n=$(tr -d '-' <<<"$video_asset")
find "$PWD" -type f -path "*${asset_n}*visual*" -print >"$work/visual-files-before.txt" || true
[[ -s "$work/visual-files-before.txt" ]]
req admin DELETE "/api/v1/admin/assets/$video_asset" >"$work/delete.json"
python3 -c 'import json,sys;d=json.load(open(sys.argv[1]));assert d["deleted"] and d["databaseRecordsDeleted"]' "$work/delete.json"
if find "$PWD" -type f -path "*${asset_n}*visual*" -print | grep -q .; then echo 'visual thumbnail survived permanent deletion' >&2; exit 1; fi
code=$(curl -sS -o /dev/null -w '%{http_code}' -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12VisualAcceptance' "$api_url/api/v1/discovery/assets/$video_asset/visual-segments/$segment_id/thumbnail"); [[ "$code" == 404 ]]
VIDEO_ASSET="$video_asset" image_search viewer "$work/video-query.jpg" | VIDEO_ASSET="$video_asset" python3 -c 'import json,sys,os;assert all(x["assetId"]!=os.environ["VIDEO_ASSET"] for x in json.load(sys.stdin)["items"])'

# New Web assets must parse and contain both Arabic and English surfaces.
node --check src/MAM.Web/wwwroot/p12-visual-search.js
grep -q 'Search by Image' src/MAM.Web/wwwroot/p12-visual-search.js
grep -q 'البحث بالصورة' src/MAM.Web/wwwroot/p12-visual-search.js
grep -q 'Transcript & Visual Segments' src/MAM.Web/wwwroot/p12-visual-search.js
grep -q 'التفريغ والمقاطع المرئية' src/MAM.Web/wwwroot/p12-visual-search.js
grep -q 'p12-visual-search.js' src/MAM.Web/wwwroot/index.html
grep -q 'p12-visual-search.css' src/MAM.Web/wwwroot/index.html

# Fail-closed provider path: same authoritative data, provider explicitly disabled.
stop_api
export MAM_VISUAL_PROVIDER_DISABLED=true
start_api disabled
code=$(curl -sS -o "$work/disabled-health.json" -w '%{http_code}' "$api_url/health/visual-search"); [[ "$code" == 503 ]]
python3 -c 'import json,sys;d=json.load(open(sys.argv[1]));assert d["status"]=="Degraded" and not d["visual"]["isReady"]' "$work/disabled-health.json"
code=$(curl -sS -o "$work/disabled-search.json" -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12VisualAcceptance' -H 'Content-Type: image/png' --data-binary @"$work/white.png" "$api_url/api/v1/discovery/image-search"); [[ "$code" == 503 ]]
unset MAM_VISUAL_PROVIDER_DISABLED

echo 'PASS: P12 visual segment/image-search SQL acceptance verified: validation, no-match, image index, video thumbnails, permissions, idempotency, deletion cleanup, bilingual UI and fail-closed provider.'
