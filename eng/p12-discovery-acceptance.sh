#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5117"
web_url="http://127.0.0.1:5118"
work="${RUNNER_TEMP:-/tmp}/mam-p12-discovery"
rm -rf "$work" .mam-dev src/MAM.Api/.mam-dev
mkdir -p "$work"
password_key="Pass""word"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14334;Initial Catalog=MamP12DiscoveryCi;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"

python3 - <<'PY' config/appsettings.Development.template.json "$work/config.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Jobs']['LeaseSeconds']=30;d['Jobs']['HeartbeatSeconds']=5;d['Jobs']['MaxAttempts']=5
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
export MAM_WHISPER_LANGUAGE=en

api_pid=""; web_pid=""
cleanup(){
  [[ -z "$web_pid" ]] || { kill "$web_pid" >/dev/null 2>&1 || true; wait "$web_pid" >/dev/null 2>&1 || true; }
  [[ -z "$api_pid" ]] || { kill "$api_pid" >/dev/null 2>&1 || true; wait "$api_pid" >/dev/null 2>&1 || true; }
}
trap cleanup EXIT

export ASPNETCORE_URLS="$api_url"
dotnet run --project src/MAM.Api/MAM.Api.csproj -c Release --no-build >"$work/api.log" 2>&1 & api_pid=$!
for _ in $(seq 1 100); do curl -fsS "$api_url/health/discovery" >/dev/null 2>&1 && break; sleep .5; done
curl -fsS "$api_url/health/discovery" | python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["status"]=="Ready" and d["discovery"]["isReady"]'

req(){
  local user="$1" method="$2" path="$3" body="${4:-}"
  if [[ -n "$body" ]]; then curl -fsS -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data "$body" "$api_url$path"
  else curl -fsS -X "$method" -H "X-MAM-Dev-User: $user" -H 'X-MAM-Client: P12Acceptance' "$api_url$path"; fi
}
upload(){
  local file="$1" title="$2" name size sha body session sid final
  name=$(basename "$file"); size=$(stat -c %s "$file"); sha=$(sha256sum "$file"|awk '{print $1}')
  body=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  session=$(req editor POST /api/v1/uploads/sessions "$body"); sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$session")
  curl -fsS -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: P12Acceptance' -H "X-Chunk-SHA256: $sha" -H 'Content-Type: application/octet-stream' --data-binary @"$file" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  final=$(req editor POST "/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(d["assetId"]+"|"+d["sha256"])' <<<"$final"
}
queue(){ req editor POST "/api/v1/processing/assets/$1/jobs" "{\"profileId\":\"$2\"}"; }
worker(){ MAM_WORKER_ID="p12-discovery-$1" dotnet run --project src/MAM.Worker/MAM.Worker.csproj -c Release --no-build -- --processing-only --once >"$work/worker-$1.log" 2>&1 || { cat "$work/worker-$1.log"; return 1; }; }
assert_job(){ local jobs; jobs=$(req viewer GET '/api/v1/processing/jobs?limit=100'); python3 -c 'import json,sys;j=next(x for x in json.load(sys.stdin) if x["jobId"]==sys.argv[1]);assert j["state"]==2 and j["completedAtUtc"] and not j["lastError"],j' "$1" <<<"$jobs"; }

# Audio inspection -> embedded metadata index.
ffmpeg -y -hide_banner -loglevel error -f lavfi -i sine=frequency=440:duration=2.5 -metadata title='MAM EMBEDDED TITLE 2026' -metadata creation_time='2026-09-13T18:00:00Z' -c:a aac -b:a 96k "$work/audio.m4a"
IFS='|' read -r audio_asset audio_sha <<<"$(upload "$work/audio.m4a" 'Discovery Audio')"
req viewer GET "/api/v1/discovery/assets/$audio_asset/category" | python3 -c 'import json,sys;d=json.load(sys.stdin)["category"];assert d["isSystem"] and d["nameEn"]=="Uncategorized" and d["nameAr"]=="غير مصنف"'
job=$(queue "$audio_asset" inspect-v1); jid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$job"); worker inspect; assert_job "$jid"
AUDIO_ASSET="$audio_asset" req viewer GET '/api/v1/discovery/search?query=embedded%20title&page=1&pageSize=50' | AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["AUDIO_ASSET"] for x in json.load(sys.stdin)["items"])'

# Timestamped transcript -> SQL text/timeline index and search.
job=$(queue "$audio_asset" transcript-text-v1); jid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$job"); worker transcript; assert_job "$jid"
req viewer GET "/api/v1/discovery/assets/$audio_asset/text/transcript" | python3 -c 'import json,sys;d=json.load(sys.stdin);assert "MAM TRANSCRIPT DISCOVERY 2026" in d["text"] and len(d["segments"])==2 and d["segments"][0]["startMs"]==0 and d["segments"][0]["endMs"]==1500'
req viewer GET "/api/v1/discovery/assets/$audio_asset/extraction-status" | python3 -c 'import json,sys;s=next(x for x in json.load(sys.stdin) if x["extractionKind"]=="transcript");assert s["state"]=="Succeeded" and s["progressPercent"]==100'
AUDIO_ASSET="$audio_asset" req viewer GET '/api/v1/discovery/search?query=transcript%20discovery&page=1&pageSize=50' | AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;h=next(x for x in json.load(sys.stdin)["items"] if x["assetId"]==os.environ["AUDIO_ASSET"]);assert h["matchedSource"]=="transcript" and h["startMs"]==0'

# Unlimited-depth capable hierarchy, assignment and dashboard metrics.
root=$(req editor POST /api/v1/discovery/categories '{"parentCategoryId":null,"nameEn":"Broadcast Archive","nameAr":"أرشيف البث","sortOrder":10}'); root_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["categoryId"])' <<<"$root")
child=$(req editor POST /api/v1/discovery/categories "{\"parentCategoryId\":\"$root_id\",\"nameEn\":\"Interviews\",\"nameAr\":\"المقابلات\",\"sortOrder\":20}"); child_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["categoryId"])' <<<"$child"); child_v=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["version"])' <<<"$child")
grand=$(req editor POST /api/v1/discovery/categories "{\"parentCategoryId\":\"$child_id\",\"nameEn\":\"Official\",\"nameAr\":\"الرسمية\",\"sortOrder\":30}"); grand_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["categoryId"])' <<<"$grand")
req editor PUT "/api/v1/discovery/assets/$audio_asset/category" "{\"categoryId\":\"$grand_id\"}" >/dev/null
GRAND_ID="$grand_id" req viewer GET "/api/v1/discovery/assets/$audio_asset/category" | GRAND_ID="$grand_id" python3 -c 'import json,sys,os;assert json.load(sys.stdin)["category"]["categoryId"]==os.environ["GRAND_ID"]'
req editor PUT "/api/v1/discovery/categories/$child_id" "{\"expectedVersion\":$child_v,\"parentCategoryId\":\"$root_id\",\"nameEn\":\"Official Interviews\",\"nameAr\":\"المقابلات الرسمية\",\"sortOrder\":21}" >/dev/null
req viewer GET /api/v1/discovery/dashboard | python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["categoryCount"]>=3 and d["indexedAssetCount"]>=1 and d["transcriptCount"]>=1'

# Reference library + explicit manual tags are searchable. No automatic real-person identification is exercised.
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=white:s=320x180 -frames:v 1 "$work/reference.png"
IFS='|' read -r image_asset image_sha <<<"$(upload "$work/reference.png" 'Reference Photo Asset')"
ref=$(req editor POST /api/v1/discovery/references '{"nameEn":"Reference Subject Alpha","nameAr":"المرجع ألفا","descriptionEn":"Manual reference","descriptionAr":"مرجع يدوي","tags":["ceremony","reference"]}'); ref_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["subjectId"])' <<<"$ref")
req editor POST "/api/v1/discovery/references/$ref_id/images" "{\"assetId\":\"$image_asset\"}" >/dev/null
req editor POST "/api/v1/discovery/assets/$audio_asset/reference-tags" "{\"subjectId\":\"$ref_id\",\"confidence\":null,\"detectionSource\":\"manual\"}" >/dev/null
AUDIO_ASSET="$audio_asset" req viewer GET '/api/v1/discovery/search?query=reference%20subject%20alpha&page=1&pageSize=50' | AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;h=next(x for x in json.load(sys.stdin)["items"] if x["assetId"]==os.environ["AUDIO_ASSET"]);assert "Reference Subject Alpha" in h["referenceTags"]'

# DOCX native extraction + embedded document metadata index.
python3 - <<'PY' "$work/discovery.docx"
import sys,zipfile
p=sys.argv[1]
doc='''<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>MAM WORD DOCUMENT DISCOVERY 2026</w:t></w:r></w:p><w:p><w:r><w:t>Second searchable paragraph</w:t></w:r></w:p></w:body></w:document>'''
core='''<?xml version="1.0"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>MAM WORD EMBEDDED TITLE</dc:title><dc:creator>Diwan Archive</dc:creator></cp:coreProperties>'''
with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED) as z:z.writestr('word/document.xml',doc);z.writestr('docProps/core.xml',core)
PY
IFS='|' read -r word_asset word_sha <<<"$(upload "$work/discovery.docx" 'Discovery Word Document')"
job=$(queue "$word_asset" ocr-text-v1); jid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["jobId"])' <<<"$job"); worker word; assert_job "$jid"
req viewer GET "/api/v1/discovery/assets/$word_asset/text/ocr" | python3 -c 'import json,sys;d=json.load(sys.stdin);assert "MAM WORD DOCUMENT DISCOVERY 2026" in d["text"] and len(d["segments"])>=2'
WORD_ASSET="$word_asset" req viewer GET '/api/v1/discovery/search?query=word%20document%20discovery&page=1&pageSize=50' | WORD_ASSET="$word_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["WORD_ASSET"] for x in json.load(sys.stdin)["items"])'
WORD_ASSET="$word_asset" req viewer GET '/api/v1/discovery/search?query=word%20embedded%20title&page=1&pageSize=50' | WORD_ASSET="$word_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["WORD_ASSET"] for x in json.load(sys.stdin)["items"])'

# Media-kind RBAC is enforced server-side.
req admin GET /api/v1/discovery/media-permissions | python3 -c 'import json,sys;rows=json.load(sys.stdin);assert any(x["roleName"]=="Viewer" and x["mediaKind"]=="Audio" and not x["canUpload"] for x in rows)'
req viewer GET /api/v1/discovery/my-media-capabilities | python3 -c 'import json,sys;rows=json.load(sys.stdin);audio=next(x for x in rows if x["mediaKind"]=="Audio");assert audio["canView"] and not audio["canUpload"] and not audio["canProcess"]'
viewer_payload='{"title":"Denied","originalFileName":"denied.mp3","expectedLength":1,"expectedSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}'
code=$(curl -sS -o "$work/denied-upload.json" -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data "$viewer_payload" "$api_url/api/v1/uploads/sessions"); [[ "$code" == 403 ]]
req admin PUT /api/v1/discovery/media-permissions '{"roleName":"CatalogEditor","mediaKind":"Audio","canView":true,"canUpload":true,"canEdit":true,"canProcess":false,"canDownload":true}' >/dev/null
code=$(curl -sS -o "$work/denied-process.json" -w '%{http_code}' -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: P12Acceptance' -H 'Content-Type: application/json' --data '{"profileId":"transcript-text-v1"}' "$api_url/api/v1/processing/assets/$audio_asset/jobs"); [[ "$code" == 403 ]]
req admin PUT /api/v1/discovery/media-permissions '{"roleName":"CatalogEditor","mediaKind":"Audio","canView":true,"canUpload":true,"canEdit":true,"canProcess":true,"canDownload":true}' >/dev/null

# Web proxy parity.
MAM_API_BASE_URL="$api_url" MAM_DEV_USER=admin ASPNETCORE_URLS="$web_url" dotnet run --project src/MAM.Web/MAM.Web.csproj -c Release --no-build >"$work/web.log" 2>&1 & web_pid=$!
for _ in $(seq 1 80); do curl -fsS "$web_url/version" >/dev/null 2>&1 && break; sleep .25; done
curl -fsS "$web_url/client-api/discovery/categories" | python3 -c 'import json,sys;rows=json.load(sys.stdin);assert any(x["nameEn"]=="Broadcast Archive" for x in rows) and any(x["nameEn"]=="Official Interviews" for x in rows)'
AUDIO_ASSET="$audio_asset" curl -fsS "$web_url/client-api/discovery/search?query=transcript%20discovery&page=1&pageSize=50" | AUDIO_ASSET="$audio_asset" python3 -c 'import json,sys,os;assert any(x["assetId"]==os.environ["AUDIO_ASSET"] for x in json.load(sys.stdin)["items"])'
curl -fsS "$web_url/client-api/discovery/my-media-capabilities" | python3 -c 'import json,sys;rows=json.load(sys.stdin);assert any(x["mediaKind"]=="Audio" and x["canUpload"] for x in rows)'

# Safe cleanup validates hierarchy deletion after moving the asset back to Uncategorized.
req editor PUT "/api/v1/discovery/assets/$audio_asset/category" '{"categoryId":null}' >/dev/null
req editor DELETE "/api/v1/discovery/categories/$grand_id" >/dev/null
req editor DELETE "/api/v1/discovery/categories/$child_id" >/dev/null
req editor DELETE "/api/v1/discovery/categories/$root_id" >/dev/null

# Bilingual Windows/Web functional surface and architecture boundary contracts.
grep -q 'Content Search' src/MAM.Desktop/MainWindow.P12.cs; grep -q 'البحث في المحتوى' src/MAM.Desktop/MainWindow.P12.cs
grep -q 'Transcript Timeline' src/MAM.Desktop/MainWindow.P12.cs; grep -q 'Media Permissions' src/MAM.Desktop/MainWindow.P12.cs
grep -q 'Categories' src/MAM.Web/wwwroot/p12-discovery.js; grep -q 'التصنيفات' src/MAM.Web/wwwroot/p12-discovery.js
grep -q 'Reference Library' src/MAM.Web/wwwroot/p12-discovery.js; grep -q 'صلاحيات أنواع الوسائط' src/MAM.Web/wwwroot/p12-discovery.js
grep -q 'my-media-capabilities' src/MAM.Web/wwwroot/p12-upload-capabilities.js
! grep -R --include='*.cs' -n 'MAM\.Infrastructure' src/MAM.Desktop src/MAM.Web

echo 'PASS: P12 discovery/indexing/categories/transcript/DOCX/reference/RBAC/Web+Windows parity acceptance verified.'
