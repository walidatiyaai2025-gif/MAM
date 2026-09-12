#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5200"
web_url="http://127.0.0.1:5201"
work="${RUNNER_TEMP:-/tmp}/mam-p05"
rm -rf "$work"
mkdir -p "$work"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_STORAGE_BASE_PATH="$PWD"

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
for _ in $(seq 1 80); do
  if curl --fail --silent "$api_url/health/curation" >/dev/null 2>&1; then ready=1; break; fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then cat "$work/api.log"; echo "FAIL: P05 API did not become curation-ready." >&2; exit 1; fi
health=$(curl --fail --silent "$api_url/health/curation")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["status"]=="Ready" and d["curation"]["isReady"] is True' <<<"$health"

status=$(curl --silent --output "$work/anon-search.json" --write-out '%{http_code}' "$api_url/api/v1/curation/search")
[[ "$status" == "401" ]] || { echo "FAIL: anonymous curation search expected 401, got $status" >&2; exit 1; }
policy=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/curation/policy")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["savedFiltersSupported"] is False and d["maxPageSize"]==100 and d["maxBulkItems"]==100' <<<"$policy"

viewer_write=$(curl --silent --output "$work/viewer-write.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'Content-Type: application/json' --data '{"nameEn":"Forbidden collection","nameAr":""}' "$api_url/api/v1/curation/collections")
[[ "$viewer_write" == "403" ]] || { echo "FAIL: Viewer collection write expected 403, got $viewer_write" >&2; exit 1; }

# Upload one representative authoritative original so P05 archive/restore can prove it never mutates Primary media.
dd if=/dev/zero of="$work/p05-original.mp4" bs=1024 count=768 status=none
printf 'P05-CURATION-ORIGINAL' | dd of="$work/p05-original.mp4" bs=1 seek=64 conv=notrunc status=none
size=$(stat -c %s "$work/p05-original.mp4")
sha=$(sha256sum "$work/p05-original.mp4" | awk '{print $1}')
create_payload=$(python3 - <<'PY' "$size" "$sha"
import json,sys
print(json.dumps({"title":"P05 Original","originalFileName":"p05-original.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))
PY
)
created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H 'Content-Type: application/json' --data "$create_payload" "$api_url/api/v1/uploads/sessions")
session_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H "X-Chunk-SHA256: $sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/p05-original.mp4" "$api_url/api/v1/uploads/sessions/$session_id/chunks?offset=0" >/dev/null
finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' "$api_url/api/v1/uploads/sessions/$session_id/finalize")
asset1=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["assetId"])' <<<"$finalized")
object_key=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["primaryObjectKey"])' <<<"$finalized")
primary_path="$PWD/.mam-dev/primary/$object_key"
[[ -f "$primary_path" ]] || { echo "FAIL: representative Primary original missing: $primary_path" >&2; exit 1; }
primary_before=$(sha256sum "$primary_path" | awk '{print $1}')
[[ "$primary_before" == "$sha" ]] || { echo "FAIL: representative Primary original hash mismatch before P05." >&2; exit 1; }

create_asset() {
  local title="$1"
  curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1]}))' "$title")" "$api_url/api/v1/catalog/assets"
}
asset2_json=$(create_asset "P05 Paging Alpha")
asset3_json=$(create_asset "P05 Paging Beta")
asset2=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["id"])' <<<"$asset2_json")
asset3=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["id"])' <<<"$asset3_json")

metadata_body() {
  python3 - <<'PY' "$1" "$2" "$3" "$4" "$5" "$6"
import json,sys
version=int(sys.argv[1]); title=sys.argv[2]; title_ar=sys.argv[3]; category=sys.argv[4]; tags=sys.argv[5].split('|') if sys.argv[5] else []; notes=sys.argv[6]
print(json.dumps({"expectedVersion":version,"schemaKey":"core-media-v1","titleEn":title,"titleAr":title_ar,"eventDate":"2026-09-12","category":category,"tags":tags,"preservationNotes":notes},ensure_ascii=False))
PY
}
meta1=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$(metadata_body 1 'Royal Reception 2026' 'الإمارة تستقبل الوفد' 'Ceremony' 'ديوان|Reception' 'Official reception archive')" "$api_url/api/v1/curation/assets/$asset1/metadata")
meta2=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$(metadata_body 1 'P05 Paging Alpha' 'أرشيف ألف' 'Ceremony' 'Archive|Alpha' 'Paging acceptance alpha')" "$api_url/api/v1/curation/assets/$asset2/metadata")
meta3=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$(metadata_body 1 'P05 Paging Beta' 'أرشيف باء' 'Interview' 'Archive|Beta' 'Paging acceptance beta')" "$api_url/api/v1/curation/assets/$asset3/metadata")
python3 -c 'import json,sys;assert json.load(sys.stdin)["version"]==2' <<<"$meta1"

english=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'query=reception' "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);target=sys.argv[1];assert any(x["id"]==target for x in d["items"])' "$asset1" <<<"$english"
arabic=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'query=الاماره' "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);target=sys.argv[1];assert any(x["id"]==target for x in d["items"])' "$asset1" <<<"$arabic"
category_search=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'category=CEREMONY' "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);ids={x["id"] for x in d["items"]};assert set(sys.argv[1:3])<=ids and any(x["value"]=="Ceremony" and x["count"]>=2 for x in d["facets"]["categories"])' "$asset1" "$asset2" <<<"$category_search"
tag_search=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'tag=ديوان' "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["id"]==sys.argv[1] for x in d["items"])' "$asset1" <<<"$tag_search"

page1=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'query=P05 Paging' --data 'page=1&pageSize=1' "$api_url/api/v1/curation/search")
page2=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode 'query=P05 Paging' --data 'page=2&pageSize=1' "$api_url/api/v1/curation/search")
python3 - <<'PY' "$page1" "$page2"
import json,sys
a=json.loads(sys.argv[1]);b=json.loads(sys.argv[2]);assert a['totalCount']>=2 and len(a['items'])==1 and len(b['items'])==1 and a['items'][0]['id']!=b['items'][0]['id'] and a['page']==1 and b['page']==2 and a['pageSize']==1
PY

stale_status=$(curl --silent --output "$work/stale.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$(metadata_body 1 'Stale overwrite' '' 'Ceremony' 'Bad' 'Must fail')" "$api_url/api/v1/curation/assets/$asset1/metadata")
[[ "$stale_status" == "409" ]] || { echo "FAIL: stale metadata update expected 409, got $stale_status" >&2; exit 1; }
python3 -c 'import json,sys;d=json.load(open(sys.argv[1]));assert d["error"]=="concurrency_conflict" and d["current"]["version"]==2' "$work/stale.json"

bulk_payload=$(python3 - <<'PY' "$asset1" "$asset2"
import json,sys
items=[
 {"assetId":sys.argv[1],"expectedVersion":1,"schemaKey":"core-media-v1","titleEn":"Must Not Overwrite","titleAr":"","eventDate":"2026-09-12","category":"Ceremony","tags":["Bad"],"preservationNotes":"stale"},
 {"assetId":sys.argv[2],"expectedVersion":2,"schemaKey":"core-media-v1","titleEn":"P05 Paging Alpha Bulk","titleAr":"أرشيف ألف","eventDate":"2026-09-12","category":"Ceremony","tags":["Archive","Alpha","Bulk"],"preservationNotes":"bulk success"}
]
print(json.dumps({"items":items},ensure_ascii=False))
PY
)
bulk=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "$bulk_payload" "$api_url/api/v1/curation/assets/bulk-metadata")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["requested"]==2 and d["succeeded"]==1 and d["failed"]==1 and any((not x["succeeded"]) and x["status"]=="concurrency_conflict" for x in d["results"])' <<<"$bulk"

collection=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data '{"nameEn":"Royal Events","nameAr":"المناسبات الرسمية"}' "$api_url/api/v1/curation/collections")
collection_id=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["collectionId"])' <<<"$collection")
collection_v=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["version"])' <<<"$collection")
added=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "{\"expectedVersion\":$collection_v}" "$api_url/api/v1/curation/collections/$collection_id/assets/$asset1")
collection_v2=$(python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["memberCount"]==1;print(d["version"])' <<<"$added")
stale_collection=$(curl --silent --output "$work/stale-collection.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data "{\"expectedVersion\":$collection_v}" "$api_url/api/v1/curation/collections/$collection_id/assets/$asset2")
[[ "$stale_collection" == "409" ]] || { echo "FAIL: stale collection membership expected 409, got $stale_collection" >&2; exit 1; }
collection_search=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data-urlencode "collectionId=$collection_id" "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["id"]==sys.argv[1] for x in d["items"])' "$asset1" <<<"$collection_search"

viewer_archive=$(curl --silent --output "$work/viewer-archive.json" --write-out '%{http_code}' -X POST -H 'X-MAM-Dev-User: viewer' -H 'Content-Type: application/json' --data '{"expectedVersion":2}' "$api_url/api/v1/curation/assets/$asset1/archive")
[[ "$viewer_archive" == "403" ]] || { echo "FAIL: Viewer archive expected 403, got $viewer_archive" >&2; exit 1; }
archived=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data '{"expectedVersion":2}' "$api_url/api/v1/curation/assets/$asset1/archive")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["lifecycle"]=="Archived" and d["version"]==3' <<<"$archived"
primary_archived=$(sha256sum "$primary_path" | awk '{print $1}')
[[ "$primary_archived" == "$primary_before" ]] || { echo "FAIL: archive lifecycle mutated the Primary original." >&2; exit 1; }
archived_search=$(curl --fail --silent -G -H 'X-MAM-Dev-User: viewer' --data 'lifecycle=Archived' "$api_url/api/v1/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["id"]==sys.argv[1] for x in d["items"])' "$asset1" <<<"$archived_search"
restored=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' --data '{"expectedVersion":3}' "$api_url/api/v1/curation/assets/$asset1/restore")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["lifecycle"]=="Active" and d["version"]==4' <<<"$restored"
primary_restored=$(sha256sum "$primary_path" | awk '{print $1}')
[[ "$primary_restored" == "$primary_before" ]] || { echo "FAIL: restore lifecycle mutated the Primary original." >&2; exit 1; }

# Actual Web server must observe the same authoritative search/collection/metadata state through its Central API proxy.
export MAM_API_BASE_URL="$api_url/"
export MAM_DEV_USER=viewer
ASPNETCORE_URLS="$web_url" dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
web_ready=0
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep .5
done
if [[ "$web_ready" != "1" ]]; then cat "$work/web.log"; echo "FAIL: P05 Web proxy did not start." >&2; exit 1; fi
web_arabic=$(curl --fail --silent -G --data-urlencode 'query=الاماره' "$web_url/client-api/curation/search")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["id"]==sys.argv[1] for x in d["items"])' "$asset1" <<<"$web_arabic"
web_collections=$(curl --fail --silent "$web_url/client-api/curation/collections")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["collectionId"]==sys.argv[1] and x["memberCount"]>=1 for x in d)' "$collection_id" <<<"$web_collections"
web_metadata=$(curl --fail --silent "$web_url/client-api/curation/assets/$asset1/metadata")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["titleAr"]=="الإمارة تستقبل الوفد" and d["lifecycle"]=="Active"' <<<"$web_metadata"

# Audit must contain curation success/partial/lifecycle evidence.
audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/audit/recent?limit=100")
python3 -c 'import json,sys;events=json.load(sys.stdin);actions={x["action"] for x in events};required={"curation.metadata.updated","curation.metadata.bulk","curation.collection.created","curation.collection.asset-added","curation.asset.archived","curation.asset.restored"};assert required<=actions,(required-actions)' <<<"$audit"

echo "PASS: P05 authoritative English/Arabic search normalization, facets, deterministic pagination, metadata concurrency, explicit bulk partial failure, collections, permission negatives, non-destructive archive/restore, Web shared visibility, policy and audit acceptance succeeded."
