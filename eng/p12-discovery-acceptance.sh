#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5117"
web_url="http://127.0.0.1:5118"
work="${RUNNER_TEMP:-/tmp}/mam-p12-discovery"
rm -rf "$work" .mam-dev src/MAM.Api/.mam-dev
mkdir -p "$work"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14334;Initial Catalog=MamP12DiscoveryCi;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"

python3 - <<'PY' config/appsettings.Development.template.json "$work/discovery.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Jobs']['LeaseSeconds']=30
d['Jobs']['HeartbeatSeconds']=5
d['Jobs']['MaxAttempts']=5
with open(sys.argv[2],'w',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/discovery.json"

cat >"$work/fake-whisper" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
out=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -of) shift; out="${1:-}" ;;
  esac
  shift || true
done
[[ -n "$out" ]] || { echo "missing -of" >&2; exit 2; }
cat >"${out}.vtt" <<'VTT'
WEBVTT

00:00:00.000 --> 00:00:01.500
MAM TRANSCRIPT DISCOVERY 2026

00:00:01.500 --> 00:00:02.500
SECOND TIMELINE SEGMENT
VTT
SH
chmod +x "$work/fake-whisper"
printf 'fake-model-for-ci' >"$work/fake-model.bin"
export MAM_WHISPER_PATH="$work/fake-whisper"
export MAM_WHISPER_MODEL_PATH="$work/fake-model.bin"
export MAM_WHISPER_LANGUAGE="en"

api_pid=""
web_pid=""
cleanup() {
  if [[ -n "$web_pid" ]]; then kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; fi
  if [[ -n "$api_pid" ]]; then kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

export ASPNETCORE_URLS="$api_url"
dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api.log" 2>&1 &
api_pid=$!
ready=0
for _ in $(seq 1 100); do
  if curl --fail --silent "$api_url/health/discovery" >/dev/null 2>&1; then ready=1; break; fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then
  cat "$work/api.log"
  echo "FAIL: P12 discovery API did not become ready." >&2
  exit 1
fi

health=$(curl --fail --silent "$api_url/health/discovery")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["status"]=="Ready" and d["discovery"]["isReady"] is True' <<<"$health"

api_json() {
  local user="$1" method="$2" path="$3" body="${4:-}"
  if [[ -n "$body" ]]; then
    curl --fail --silent -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data "$body" "$api_url$path"
  else
    curl --fail --silent -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12Acceptance' "$api_url$path"
  fi
}

upload_file() {
  local path="$1" title="$2"
  local name size sha payload created sid finalized
  name=$(basename "$path")
  size=$(stat -c %s "$path")
  sha=$(sha256sum "$path" | awk '{print $1}')
  payload=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  created=$(api_json editor POST /api/v1/uploads/sessions "$payload")
  sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
  curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: P12Acceptance' -H "X-Chunk-SHA256: $sha" -H 'Content-Type: application/octet-stream' --data-binary @"$path" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  finalized=$(api_json editor POST "/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(str(d["assetId"])+"|"+d["sha256"])' <<<"$finalized"
}

queue_job() {
  local asset="$1" profile="$2"
  api_json editor POST "/api/v1/processing/assets/$asset/jobs" "{\"profileId\":\"$profile\"}"
}

run_worker_once() {
  local name="$1"
  MAM_WORKER_ID="p12-discovery-$name" dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --processing-only --once >"$work/worker-$name.log" 2>&1 || {
    cat "$work/worker-$name.log"
    return 1
  }
}

assert_job_succeeded() {
  local job_id="$1"
  local jobs
  jobs=$(api_json viewer GET '/api/v1/processing/jobs?limit=100')
  python3 -c 'import json,sys;jid=sys.argv[1];j=next(x for x in json.load(sys.stdin) if x["jobId"]==jid);assert j["state"]==2 and j["completedAtUtc"] and not j["lastError"],j' "$job_id" <<<"$jobs"
}

# Media with embedded title/creation metadata, then real FFprobe inspection indexing.
ffmpeg -y -hide_banner -loglevel error -f lavfi -i sine=frequency=440:duration=2.5 \
  -metadata title='MAM EMBEDDED TITLE 2026' -metadata creation_time='2026-09-13T18:00:00Z' \
  -c:a aac -b:a 96k "$work/discovery-audio.m4a"
audio_info=$(upload_file "$work/discovery-audio.m4a" "Discovery Audio")
IFS='|' read -r audio_asset audio_sha <<<"$audio_info"

# Newly created asset must resolve to the protected system Uncategorized category.
uncategorized=$(api_json viewer GET "/api/v1/discovery/assets/$audio_asset/category")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["category"]["isSystem"] is True;assert d["category"]["nameEn"]=="Uncategorized";assert d["category"]["nameAr"]=="غير مصنف"' <<<"$uncategorized"

inspect=$(queue_job "$audio_asset" inspect-v1)
inspect_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$inspect")
run_worker_once inspect
assert_job_succeeded "$inspect_job"
metadata_search=$(api_json viewer GET '/api/v1/discovery/search?query=embedded%20title&page=1&pageSize=50')
python3 -c 'import json,sys,os;asset=os.environ["AUDIO_ASSET"];d=json.load(sys.stdin);assert any(x["assetId"]==asset for x in d["items"]),d' <<<"$metadata_search" AUDIO_ASSET="$audio_asset"

# Timestamped transcript uses a deterministic fake whisper CLI while exercising real Worker/FFmpeg/index paths.
transcript=$(queue_job "$audio_asset" transcript-text-v1)
transcript_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$transcript")
run_worker_once transcript
assert_job_succeeded "$transcript_job"
transcript_text=$(api_json viewer GET "/api/v1/discovery/assets/$audio_asset/text/transcript")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert "MAM TRANSCRIPT DISCOVERY 2026" in d["text"];assert len(d["segments"])==2;assert d["segments"][0]["startMs"]==0;assert d["segments"][0]["endMs"]==1500' <<<"$transcript_text"
transcript_status=$(api_json viewer GET "/api/v1/discovery/assets/$audio_asset/extraction-status")
python3 -c 'import json,sys;rows=json.load(sys.stdin);s=next(x for x in rows if x["extractionKind"]=="transcript");assert s["state"]=="Succeeded" and s["progressPercent"]==100' <<<"$transcript_status"
transcript_search=$(api_json viewer GET '/api/v1/discovery/search?query=transcript%20discovery&page=1&pageSize=50')
AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;asset=os.environ["AUDIO_ASSET"];d=json.load(sys.stdin);h=next(x for x in d["items"] if x["assetId"]==asset);assert h["matchedSource"]=="transcript";assert h["startMs"]==0' <<<"$transcript_search"

# Hierarchical category create -> child -> assignment -> update, with dashboard count.
root=$(api_json editor POST /api/v1/discovery/categories '{"parentCategoryId":null,"nameEn":"Broadcast Archive","nameAr":"أرشيف البث","sortOrder":10}')
root_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["categoryId"])' <<<"$root")
child=$(api_json editor POST /api/v1/discovery/categories "{\"parentCategoryId\":\"$root_id\",\"nameEn\":\"Interviews\",\"nameAr\":\"المقابلات\",\"sortOrder\":20}")
child_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["categoryId"])' <<<"$child")
child_version=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["version"])' <<<"$child")
api_json editor PUT "/api/v1/discovery/assets/$audio_asset/category" "{\"categoryId\":\"$child_id\"}" >/dev/null
assigned=$(api_json viewer GET "/api/v1/discovery/assets/$audio_asset/category")
CHILD_ID="$child_id" python3 -c 'import json,sys,os;d=json.load(sys.stdin);assert d["category"]["categoryId"]==os.environ["CHILD_ID"];assert d["category"]["nameAr"]=="المقابلات"' <<<"$assigned"
updated=$(api_json editor PUT "/api/v1/discovery/categories/$child_id" "{\"expectedVersion\":$child_version,\"parentCategoryId\":\"$root_id\",\"nameEn\":\"Official Interviews\",\"nameAr\":\"المقابلات الرسمية\",\"sortOrder\":21}")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["nameEn"]=="Official Interviews" and d["version"]>=2' <<<"$updated"
dashboard=$(api_json viewer GET /api/v1/discovery/dashboard)
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["categoryCount"]>=2;assert d["indexedAssetCount"]>=1;assert d["transcriptCount"]>=1' <<<"$dashboard"

# Reference image library and explicit/manual searchable asset tagging.
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=white:s=320x180 -frames:v 1 "$work/reference.png"
image_info=$(upload_file "$work/reference.png" "Reference Photo Asset")
IFS='|' read -r image_asset image_sha <<<"$image_info"
reference=$(api_json editor POST /api/v1/discovery/references '{"nameEn":"Reference Subject Alpha","nameAr":"المرجع ألفا","descriptionEn":"Manual reference subject","descriptionAr":"مرجع يدوي","tags":["ceremony","reference"]}')
reference_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["subjectId"])' <<<"$reference")
api_json editor POST "/api/v1/discovery/references/$reference_id/images" "{\"assetId\":\"$image_asset\"}" >/dev/null
api_json editor POST "/api/v1/discovery/assets/$audio_asset/reference-tags" "{\"subjectId\":\"$reference_id\",\"confidence\":null,\"detectionSource\":\"manual\"}" >/dev/null
reference_search=$(api_json viewer GET '/api/v1/discovery/search?query=reference%20subject%20alpha&page=1&pageSize=50')
AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;asset=os.environ["AUDIO_ASSET"];d=json.load(sys.stdin);h=next(x for x in d["items"] if x["assetId"]==asset);assert "Reference Subject Alpha" in h["referenceTags"]' <<<"$reference_search"

# Word/DOCX extraction + embedded properties + search indexing.
python3 - <<'PY' "$work/discovery.docx"
import sys,zipfile
path=sys.argv[1]
doc='''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>MAM WORD DOCUMENT DISCOVERY 2026</w:t></w:r></w:p><w:p><w:r><w:t>Second searchable paragraph</w:t></w:r></w:p></w:body></w:document>'''
core='''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/"><dc:title>MAM WORD EMBEDDED TITLE</dc:title><dc:creator>Diwan Archive</dc:creator><dcterms:created>2026-09-13T18:00:00Z</dcterms:created></cp:coreProperties>'''
with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z:
    z.writestr('word/document.xml',doc)
    z.writestr('docProps/core.xml',core)
PY
word_info=$(upload_file "$work/discovery.docx" "Discovery Word Document")
IFS='|' read -r word_asset word_sha <<<"$word_info"
word_job_json=$(queue_job "$word_asset" ocr-text-v1)
word_job=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$word_job_json")
run_worker_once word
assert_job_succeeded "$word_job"
word_text=$(api_json viewer GET "/api/v1/discovery/assets/$word_asset/text/ocr")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert "MAM WORD DOCUMENT DISCOVERY 2026" in d["text"];assert len(d["segments"])>=2' <<<"$word_text"
word_search=$(api_json viewer GET '/api/v1/discovery/search?query=word%20document%20discovery&page=1&pageSize=50')
WORD_ASSET="$word_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["WORD_ASSET"] for x in json.load(sys.stdin)["items"])' <<<"$word_search"
word_meta_search=$(api_json viewer GET '/api/v1/discovery/search?query=word%20embedded%20title&page=1&pageSize=50')
WORD_ASSET="$word_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["WORD_ASSET"] for x in json.load(sys.stdin)["items"])' <<<"$word_meta_search"

# Media-kind RBAC must enforce on the server, not only by hiding UI controls.
permissions=$(api_json admin GET /api/v1/discovery/media-permissions)
python3 -c 'import json,sys;rows=json.load(sys.stdin);assert any(x["roleName"]=="Viewer" and x["mediaKind"]=="Audio" and x["canUpload"] is False for x in rows)' <<<"$permissions"
viewer_payload='{"title":"Denied Viewer Audio","originalFileName":"denied.mp3","expectedLength":1,"expectedSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}'
viewer_code=$(curl --silent --output "$work/viewer-denied.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data "$viewer_payload" "$api_url/api/v1/uploads/sessions")
[[ "$viewer_code" == "403" ]] || { cat "$work/viewer-denied.json"; echo "FAIL: viewer audio upload was not denied server-side ($viewer_code)." >&2; exit 1; }

api_json admin PUT /api/v1/discovery/media-permissions '{"roleName":"CatalogEditor","mediaKind":"Audio","canView":true,"canUpload":true,"canEdit":true,"canProcess":false,"canDownload":true}' >/dev/null
process_code=$(curl --silent --output "$work/process-denied.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data '{"profileId":"transcript-text-v1"}' "$api_url/api/v1/processing/assets/$audio_asset/jobs")
[[ "$process_code" == "403" ]] || { cat "$work/process-denied.json"; echo "FAIL: media-type process permission was not enforced ($process_code)." >&2; exit 1; }
api_json admin PUT /api/v1/discovery/media-permissions '{"roleName":"CatalogEditor","mediaKind":"Audio","canView":true,"canUpload":true,"canEdit":true,"canProcess":true,"canDownload":true}' >/dev/null

# Web server-side proxy must expose the same Central API discovery data; clients still have no SQL/storage credentials.
MAM_API_BASE_URL="$api_url" MAM_DEV_USER=admin ASPNETCORE_URLS="$web_url" dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
web_ready=0
for _ in $(seq 1 80); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep .25
done
if [[ "$web_ready" != "1" ]]; then cat "$work/web.log"; echo "FAIL: Web proxy did not start." >&2; exit 1; fi
web_categories=$(curl --fail --silent "$web_url/client-api/discovery/categories")
python3 -c 'import json,sys;rows=json.load(sys.stdin);assert any(x["nameEn"]=="Broadcast Archive" for x in rows);assert any(x["nameEn"]=="Official Interviews" for x in rows)' <<<"$web_categories"
web_search=$(curl --fail --silent "$web_url/client-api/discovery/search?query=transcript%20discovery&page=1&pageSize=50")
AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["AUDIO_ASSET"] for x in json.load(sys.stdin)["items"])' <<<"$web_search"

# Category cleanup validates safe deletion constraints and return to Uncategorized.
api_json editor PUT "/api/v1/discovery/assets/$audio_asset/category" '{"categoryId":null}' >/dev/null
api_json editor DELETE "/api/v1/discovery/categories/$child_id" >/dev/null
api_json editor DELETE "/api/v1/discovery/categories/$root_id" >/dev/null

# Source-level bilingual/Desktop/Web contract guard for the new product surfaces.
grep -q 'Content Search' src/MAM.Desktop/MainWindow.P12.cs
grep -q 'البحث في المحتوى' src/MAM.Desktop/MainWindow.P12.cs
grep -q 'Transcript Timeline' src/MAM.Desktop/MainWindow.P12.cs
grep -q 'Categories' src/MAM.Web/wwwroot/p12-discovery.js
grep -q 'التصنيفات' src/MAM.Web/wwwroot/p12-discovery.js
grep -q 'Media Permissions' src/MAM.Web/wwwroot/p12-discovery.js
grep -q 'Reference Library' src/MAM.Web/wwwroot/p12-discovery.js

# Desktop/Web remain Central-API clients: no direct Infrastructure dependency is introduced.
! grep -R --include='*.cs' -n 'MAM\.Infrastructure' src/MAM.Desktop src/MAM.Web

echo "PASS: P12 discovery acceptance verified hierarchical categories/Uncategorized, embedded metadata indexing, timestamped transcript timeline/search, DOCX extraction/search, reference library/manual tags, media-kind RBAC, dashboard metrics, Web proxy and bilingual Windows/Web source contracts."
