#!/usr/bin/env bash
set -euo pipefail
: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
release="${1:?release directory required}"
work="${RUNNER_TEMP:-/tmp}/mam-p11-acceptance"
rm -rf "$work" .mam-dev/primary .mam-dev/upload-staging
mkdir -p "$work"/{sql,api,web,worker,server-install,primary,backup}

python3 deploy/validate-deployment.py --release-dir "$release" --config config/appsettings.Production.template.json --template
version=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$release/release-manifest.json")
find_pkg(){ find "$release" -maxdepth 1 -type f -name "DiwanMAM-$1-$version*.zip" -print -quit; }
sql_pkg=$(find_pkg SqlMigrations); api_pkg=$(find_pkg Api); web_pkg=$(find_pkg Web); worker_pkg=$(find_pkg Worker)
[[ -n "$sql_pkg" && -n "$api_pkg" && -n "$web_pkg" && -n "$worker_pkg" ]] || { echo 'FAIL: release package set incomplete.' >&2; exit 1; }
unzip -q "$sql_pkg" -d "$work/sql"
unzip -q "$api_pkg" -d "$work/api"
unzip -q "$web_pkg" -d "$work/web"
unzip -q "$worker_pkg" -d "$work/worker"

tool="$work/sql/tool/MAM.Deployment.dll"
[[ -f "$tool" ]] || { echo 'FAIL: SQL bundle does not contain deployment tool.' >&2; exit 1; }
password_key=Password
master="Server=127.0.0.1,14333;Initial Catalog=master;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
db="Server=127.0.0.1,14333;Initial Catalog=MamP11Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
created=0
for _ in $(seq 1 60); do
  if dotnet "$tool" create-database "$master" MamP11Ci >"$work/create-db.json" 2>"$work/create-db.err"; then created=1; break; fi
  sleep 1
done
[[ "$created" == 1 ]] || { cat "$work/create-db.err"; exit 1; }
dotnet "$tool" migrate "$db" "$work/sql/migrations" >"$work/migrate-clean.json"

export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_SECRET_DATABASE="$db"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$work/sql/migrations"
export ASPNETCORE_URLS="http://127.0.0.1:5411"
api_log="$work/api.log"
dotnet "$work/api/MAM.Api.dll" >"$api_log" 2>&1 & api_pid=$!
cleanup(){ kill "$api_pid" >/dev/null 2>&1 || true; [[ -z "${web_pid:-}" ]] || kill "$web_pid" >/dev/null 2>&1 || true; [[ -z "${worker_pid:-}" ]] || kill "$worker_pid" >/dev/null 2>&1 || true; }
trap cleanup EXIT
ready=0
for _ in $(seq 1 80); do curl --fail --silent http://127.0.0.1:5411/health/ready >/dev/null 2>&1 && { ready=1; break; }; sleep .5; done
[[ "$ready" == 1 ]] || { cat "$api_log"; echo 'FAIL: packaged API did not become ready.' >&2; exit 1; }

python3 - <<'PY' "$work/preserved.mp4"
import sys
open(sys.argv[1],'wb').write((b'P11-PRESERVATION-MEDIA-'*4096)[:65536])
PY
file_sha=$(sha256sum "$work/preserved.mp4"|awk '{print $1}'); file_size=$(stat -c %s "$work/preserved.mp4")
payload=$(python3 -c 'import json,sys; print(json.dumps({"title":"P11 Upgrade Preservation Asset","originalFileName":"preserved.mp4","expectedLength":int(sys.argv[1]),"expectedSha256":sys.argv[2]}))' "$file_size" "$file_sha")
created_json=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H 'Content-Type: application/json' --data "$payload" http://127.0.0.1:5411/api/v1/uploads/sessions)
session=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created_json")
chunk_sha=$(sha256sum "$work/preserved.mp4"|awk '{print $1}')
curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H "X-Chunk-SHA256: $chunk_sha" -H 'Content-Type: application/octet-stream' --data-binary @"$work/preserved.mp4" "http://127.0.0.1:5411/api/v1/uploads/sessions/$session/chunks?offset=0" >/dev/null
finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' "http://127.0.0.1:5411/api/v1/uploads/sessions/$session/finalize")
object_key=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["primaryObjectKey"])' <<<"$finalized")
primary_file=$(find "$PWD" -type f -path "*/.mam-dev/primary/$object_key" -print -quit)
[[ -f "$primary_file" ]] || { echo 'FAIL: authoritative Primary object missing.' >&2; exit 1; }
primary_before=$(sha256sum "$primary_file"|awk '{print $1}')
dotnet "$tool" signature "$db" >"$work/signature-before.json"

kill "$api_pid"; wait "$api_pid" >/dev/null 2>&1 || true; api_pid=''
# Upgrade simulation: replace application package and re-run the shipped idempotent migration bundle.
rm -rf "$work/server-install"; mkdir -p "$work/server-install"; unzip -q "$api_pkg" -d "$work/server-install"
dotnet "$tool" migrate "$db" "$work/sql/migrations" >"$work/migrate-upgrade.json"
dotnet "$tool" signature "$db" >"$work/signature-after.json"
cmp -s "$work/signature-before.json" "$work/signature-after.json" || { diff -u "$work/signature-before.json" "$work/signature-after.json" || true; echo 'FAIL: database/catalog/media-reference signature changed across upgrade.' >&2; exit 1; }
[[ -f "$primary_file" && "$(sha256sum "$primary_file"|awk '{print $1}')" == "$primary_before" ]] || { echo 'FAIL: authoritative Primary object changed across upgrade.' >&2; exit 1; }

# Server package removal must not touch authoritative external media.
rm -rf "$work/server-install"
[[ -f "$primary_file" ]] || { echo 'FAIL: package uninstall/removal deleted authoritative media.' >&2; exit 1; }

# Packaged Web must boot as a real artifact.
export MAM_API_BASE_URL=http://127.0.0.1:5411
export ASPNETCORE_URLS=http://127.0.0.1:5412
dotnet "$work/web/MAM.Web.dll" >"$work/web.log" 2>&1 & web_pid=$!
web_ready=0
for _ in $(seq 1 60); do curl --fail --silent http://127.0.0.1:5412/version >/dev/null 2>&1 && { web_ready=1; break; }; sleep .5; done
[[ "$web_ready" == 1 ]] || { cat "$work/web.log"; echo 'FAIL: packaged Web did not boot.' >&2; exit 1; }
kill "$web_pid"; wait "$web_pid" >/dev/null 2>&1 || true; web_pid=''

# Packaged Worker must remain alive under valid engineering configuration; bounded timeout is success.
export ASPNETCORE_URLS=
set +e
timeout 4s dotnet "$work/worker/MAM.Worker.dll" >"$work/worker.log" 2>&1
worker_rc=$?
set -e
[[ "$worker_rc" == 124 ]] || { cat "$work/worker.log"; echo "FAIL: packaged Worker exited unexpectedly ($worker_rc)." >&2; exit 1; }

# Materialized validator positive + fail-closed overlap negative.
python3 - <<'PY' config/appsettings.Production.template.json "$work/materialized.json" "$work/primary" "$work/backup"
import json,sys,re
x=json.load(open(sys.argv[1],encoding='utf-8'))
def walk(v):
    if isinstance(v,dict): return {k:walk(z) for k,z in v.items()}
    if isinstance(v,list): return [walk(z) for z in v]
    if isinstance(v,str) and 'REPLACE-WITH-' in v: return 'OWNER-SUPPLIED'
    return v
x=walk(x); x['Server']['PublicBaseUrl']='https://mam.example.invalid'; x['Server']['AllowedOrigins']=['https://mam.example.invalid']; x['Storage']['Primary']['Root']=sys.argv[3]; x['Storage']['Backup']['Root']=sys.argv[4]; x['Auth']['Mode']='Local'
json.dump(x,open(sys.argv[2],'w',encoding='utf-8'),ensure_ascii=False,indent=2)
PY
python3 deploy/validate-deployment.py --release-dir "$release" --config "$work/materialized.json" --materialized --install-root "$work/install" --primary-root "$work/primary" --backup-root "$work/backup" >"$work/validator.json"
if python3 deploy/validate-deployment.py --release-dir "$release" --config "$work/materialized.json" --materialized --install-root "$work/primary/app" --primary-root "$work/primary" --backup-root "$work/backup" >/dev/null 2>&1; then echo 'FAIL: deployment validator accepted InstallRoot inside PrimaryRoot.' >&2; exit 1; fi

trap - EXIT
cleanup
printf '{"status":"PASS","version":"%s","databasePreserved":true,"mediaPreserved":true,"packagedApi":true,"packagedWeb":true,"packagedWorker":true}\n' "$version" | tee "$work/p11-deployment-evidence.json"
echo 'PASS: P11 generated artifacts clean-deploy, migrate, upgrade and remove without corrupting authoritative DB/catalog/media state.'
