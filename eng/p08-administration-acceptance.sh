#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5230"
web_url="http://127.0.0.1:5231"
work="${RUNNER_TEMP:-/tmp}/mam-p08"
rm -rf "$work" && mkdir -p "$work"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_SECRET_PRIMARY_STORAGE="p08-ci-secret-material-never-return-this-value"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_STORAGE_BASE_PATH="$PWD"

api_pid=""; web_pid=""
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
  if curl --fail --silent "$api_url/health/administration" >"$work/health.json" 2>/dev/null; then ready=1; break; fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then cat "$work/api.log"; echo "FAIL: P08 API did not become administration-ready." >&2; exit 1; fi
python3 -c 'import json;d=json.load(open("'$work'/health.json"));assert d["status"]=="Ready" and d["administration"]["isReady"] is True'
root=$(curl --fail --silent "$api_url/")
python3 -c 'import json,sys;assert json.load(sys.stdin)["phase"]=="P08"' <<<"$root"

anon=$(curl --silent --output "$work/anon.json" --write-out '%{http_code}' "$api_url/api/v1/admin/overview")
[[ "$anon" == "401" ]] || { echo "FAIL: anonymous admin expected 401, got $anon" >&2; exit 1; }
viewer=$(curl --silent --output "$work/viewer.json" --write-out '%{http_code}' -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/admin/overview")
[[ "$viewer" == "403" ]] || { echo "FAIL: viewer admin expected 403, got $viewer" >&2; exit 1; }

overview=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/admin/overview")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["policies"]>=10 and d["enabledPolicies"]>=9 and d["dictionaryEntries"]>=2 and d["restartRequired"]>=1' <<<"$overview"
policies=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/admin/policies")
printf '%s' "$policies" >"$work/policies.json"
python3 - "$work/policies.json" <<'PY'
import json,sys
items=json.load(open(sys.argv[1])); keys={x['policyKey'] for x in items}
required={'identity.authorization','metadata.core','capture.approved','processing.default','storage.primary','storage.backup','auth.production','retention.default','branding.diwan','notification.operations','system.runtime'}
assert required <= keys
secret_keys={'password','passwd','clientsecret','apikey','token','accesstoken','refreshtoken','connectionstring','privatekey','secret','credential','credentials'}
for policy in items:
    raw=json.dumps(policy, ensure_ascii=False).lower()
    assert 'p08-ci-secret-material-never-return-this-value' not in raw
    payload=policy.get('payload') or {}
    if isinstance(payload, dict):
        assert not ({str(k).lower() for k in payload.keys()} & secret_keys)
PY

storage_version=$(python3 -c 'import json,sys;print(next(x for x in json.load(open(sys.argv[1])) if x["policyKey"]=="storage.primary")["version"])' "$work/policies.json")
storage_payload=$(python3 - "$storage_version" <<'PY'
import json,sys
print(json.dumps({
 "expectedVersion":int(sys.argv[1]),"category":"Storage","displayNameEn":"Primary Storage reference","displayNameAr":"مرجع التخزين الأساسي",
 "payload":{"targetId":"Primary","mode":"ServerManaged","credentialHandling":"SecretReferenceOnly"},
 "secretRef":"env:MAM_SECRET_PRIMARY_STORAGE","requiresRestart":True,"isEnabled":True
},ensure_ascii=False))
PY
)
validated=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$storage_payload" "$api_url/api/v1/admin/policies/storage.primary/validate")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["valid"] is True and d["errors"]==[] and d["requiresRestart"] is True' <<<"$validated"
saved=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$storage_payload" "$api_url/api/v1/admin/policies/storage.primary")
storage_new_version=$(python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["secretRef"]=="env:MAM_SECRET_PRIMARY_STORAGE" and d["requiresRestart"] is True;print(d["version"])' <<<"$saved")
[[ "$storage_new_version" -gt "$storage_version" ]] || { echo "FAIL: storage policy version did not advance" >&2; exit 1; }

tested=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/admin/policies/storage.primary/test")
[[ "$tested" != *"p08-ci-secret-material-never-return-this-value"* ]] || { echo "FAIL: resolved secret leaked in test response" >&2; exit 1; }
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["success"] is True and d["secretReferenceConfigured"] is True and d["secretReferenceResolvable"] is True and d["code"]=="secret_reference_resolved"' <<<"$tested"

stale=$(curl --silent --output "$work/stale-policy.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$storage_payload" "$api_url/api/v1/admin/policies/storage.primary")
[[ "$stale" == "409" ]] || { echo "FAIL: stale policy write expected 409, got $stale" >&2; exit 1; }
python3 -c 'import json;d=json.load(open("'$work'/stale-policy.json"));assert d["error"]=="concurrency_conflict" and d["current"]["version"]=='"$storage_new_version"''

inline_secret=$(python3 - "$storage_new_version" <<'PY'
import json,sys
sensitive_key='pass'+'word'
payload={"targetId":"Primary",sensitive_key:"NeverPersistMe"}
print(json.dumps({"expectedVersion":int(sys.argv[1]),"category":"Storage","displayNameEn":"Primary Storage reference","displayNameAr":"مرجع التخزين الأساسي","payload":payload,"secretRef":"env:MAM_SECRET_PRIMARY_STORAGE","requiresRestart":True,"isEnabled":True},ensure_ascii=False))
PY
)
invalid_secret_status=$(curl --silent --output "$work/inline-secret.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$inline_secret" "$api_url/api/v1/admin/policies/storage.primary")
[[ "$invalid_secret_status" == "400" ]] || { echo "FAIL: inline secret policy expected 400, got $invalid_secret_status" >&2; exit 1; }
[[ "$(cat "$work/inline-secret.json")" != *"NeverPersistMe"* ]] || { echo "FAIL: inline secret echoed by API error" >&2; exit 1; }

identity_version=$(python3 -c 'import json,sys;print(next(x for x in json.load(open(sys.argv[1])) if x["policyKey"]=="identity.authorization")["version"])' "$work/policies.json")
local_password_payload=$(python3 - "$identity_version" <<'PY'
import json,sys
print(json.dumps({"expectedVersion":int(sys.argv[1]),"category":"Identity","displayNameEn":"Identity & authorization","displayNameAr":"الهوية والصلاحيات","payload":{"provisioningAuthority":"ExternalIdP","localPasswordsAllowed":True,"defaultRole":"Viewer"},"secretRef":None,"requiresRestart":True,"isEnabled":True},ensure_ascii=False))
PY
)
local_password_status=$(curl --silent --output "$work/local-password.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$local_password_payload" "$api_url/api/v1/admin/policies/identity.authorization")
[[ "$local_password_status" == "400" ]] || { echo "FAIL: local password authority expected 400, got $local_password_status" >&2; exit 1; }

retention=$(python3 -c 'import json,sys;print(next(x for x in json.load(open(sys.argv[1])) if x["policyKey"]=="retention.default")["version"])' "$work/policies.json")
bad_retention=$(python3 - "$retention" <<'PY'
import json,sys
print(json.dumps({"expectedVersion":int(sys.argv[1]),"category":"Retention","displayNameEn":"Retention & delete policy","displayNameAr":"سياسة الاحتفاظ والحذف","payload":{"retentionDays":0,"deleteMode":"OwnerApprovedDelete"},"secretRef":None,"requiresRestart":False,"isEnabled":True},ensure_ascii=False))
PY
)
ret_status=$(curl --silent --output "$work/bad-retention.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$bad_retention" "$api_url/api/v1/admin/policies/retention.default")
[[ "$ret_status" == "400" ]] || { echo "FAIL: invalid retention expected 400, got $ret_status" >&2; exit 1; }

branding=$(python3 -c 'import json,sys;print(next(x for x in json.load(open(sys.argv[1])) if x["policyKey"]=="branding.diwan")["version"])' "$work/policies.json")
bad_branding=$(python3 - "$branding" <<'PY'
import json,sys
print(json.dumps({"expectedVersion":int(sys.argv[1]),"category":"Branding","displayNameEn":"Diwan Al Amiri branding","displayNameAr":"هوية الديوان الأميري","payload":{"crestSha256":"deadbeef","navy":"#000000","gold":"#FFFFFF","identityLocked":False},"secretRef":None,"requiresRestart":False,"isEnabled":True},ensure_ascii=False))
PY
)
brand_status=$(curl --silent --output "$work/bad-branding.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$bad_branding" "$api_url/api/v1/admin/policies/branding.diwan")
[[ "$brand_status" == "400" ]] || { echo "FAIL: invalid branding expected 400, got $brand_status" >&2; exit 1; }

capture=$(python3 -c 'import json,sys;print(next(x for x in json.load(open(sys.argv[1])) if x["policyKey"]=="capture.approved")["version"])' "$work/policies.json")
bad_capture=$(python3 - "$capture" <<'PY'
import json,sys
print(json.dumps({"expectedVersion":int(sys.argv[1]),"category":"Capture","displayNameEn":"Capture station policy","displayNameAr":"سياسة محطات التسجيل","payload":{"stationPolicyId":"unsafe","simulatorAllowedProduction":True},"secretRef":None,"requiresRestart":True,"isEnabled":True},ensure_ascii=False))
PY
)
cap_status=$(curl --silent --output "$work/bad-capture.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$bad_capture" "$api_url/api/v1/admin/policies/capture.approved")
[[ "$cap_status" == "400" ]] || { echo "FAIL: Production simulator policy expected 400, got $cap_status" >&2; exit 1; }

user_id=$(python3 -c 'import uuid;print(uuid.uuid4())')
user_create='{"expectedVersion":0,"userName":"p08-admin-user","displayName":"P08 Admin User","externalSubject":"external:p08-admin","isEnabled":true,"roles":["CatalogEditor"]}'
user1=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$user_create" "$api_url/api/v1/admin/users/$user_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["version"]==1 and d["roles"]==["CatalogEditor"]' <<<"$user1"
user_update='{"expectedVersion":1,"userName":"p08-admin-user","displayName":"P08 Admin User Updated","externalSubject":"external:p08-admin","isEnabled":true,"roles":["CatalogEditor","Viewer"]}'
user2=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$user_update" "$api_url/api/v1/admin/users/$user_id")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["version"]==2 and set(d["roles"])=={"CatalogEditor","Viewer"}' <<<"$user2"
user_stale=$(curl --silent --output "$work/user-stale.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data "$user_update" "$api_url/api/v1/admin/users/$user_id")
[[ "$user_stale" == "409" ]] || { echo "FAIL: stale user update expected 409, got $user_stale" >&2; exit 1; }

entry1=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data '{"expectedVersion":0,"labelEn":"Broadcast","labelAr":"بث","isEnabled":true}' "$api_url/api/v1/admin/dictionaries/source/broadcast")
python3 -c 'import json,sys;assert json.load(sys.stdin)["version"]==1' <<<"$entry1"
entry2=$(curl --fail --silent -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data '{"expectedVersion":1,"labelEn":"Broadcast Source","labelAr":"مصدر البث","isEnabled":true}' "$api_url/api/v1/admin/dictionaries/source/broadcast")
python3 -c 'import json,sys;assert json.load(sys.stdin)["version"]==2' <<<"$entry2"
entry_stale=$(curl --silent --output "$work/entry-stale.json" --write-out '%{http_code}' -X PUT -H 'X-MAM-Dev-User: admin' -H 'Content-Type: application/json' --data '{"expectedVersion":1,"labelEn":"Bad stale","labelAr":"قديم","isEnabled":true}' "$api_url/api/v1/admin/dictionaries/source/broadcast")
[[ "$entry_stale" == "409" ]] || { echo "FAIL: stale dictionary update expected 409, got $entry_stale" >&2; exit 1; }

users=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/admin/users")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["userName"]=="p08-admin-user" and x["version"]==2 for x in d)' <<<"$users"
dictionary=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/admin/dictionaries/source")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert any(x["entryKey"]=="broadcast" and x["version"]==2 for x in d)' <<<"$dictionary"

audit=$(curl --fail --silent -G -H 'X-MAM-Dev-User: admin' --data-urlencode 'action=administration.' --data 'limit=200' "$api_url/api/v1/admin/audit")
printf '%s' "$audit" >"$work/audit.json"
python3 - "$work/audit.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1])); actions={x['action'] for x in d['items']}; outcomes={x['outcome'] for x in d['items']}
assert 'administration.policy.updated' in actions
assert 'administration.policy.rejected' in actions
assert 'administration.policy.conflict' in actions
assert 'administration.user.updated' in actions
assert 'administration.dictionary.updated' in actions
assert 'Conflict' in outcomes and 'Rejected' in outcomes and 'Success' in outcomes
PY
csv=$(curl --fail --silent -G -H 'X-MAM-Dev-User: admin' --data-urlencode 'action=administration.' --data 'limit=200' "$api_url/api/v1/admin/audit/export")
[[ "$csv" == OccurredAtUtc,* ]] || { echo "FAIL: audit CSV header missing" >&2; exit 1; }
[[ "$csv" == *"administration.policy.updated"* ]] || { echo "FAIL: audit CSV missing P08 mutation" >&2; exit 1; }
[[ "$csv" != *"p08-ci-secret-material-never-return-this-value"* ]] || { echo "FAIL: secret leaked in audit CSV" >&2; exit 1; }

export MAM_API_BASE_URL="$api_url"
export MAM_DEV_USER=admin
export ASPNETCORE_URLS="$web_url"
dotnet run --project src/MAM.Web/MAM.Web.csproj --configuration Release --no-build >"$work/web.log" 2>&1 &
web_pid=$!
web_ready=0
for _ in $(seq 1 60); do
  if curl --fail --silent "$web_url/version" >/dev/null 2>&1; then web_ready=1; break; fi
  sleep .5
done
if [[ "$web_ready" != "1" ]]; then cat "$work/web.log"; echo "FAIL: P08 Web did not become ready." >&2; exit 1; fi
web_overview=$(curl --fail --silent "$web_url/client-api/admin/overview")
python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["policies"]>=10' <<<"$web_overview"
web_policies=$(curl --fail --silent "$web_url/client-api/admin/policies")
[[ "$web_policies" != *"p08-ci-secret-material-never-return-this-value"* ]] || { echo "FAIL: Web proxy exposed secret material" >&2; exit 1; }
web_audit=$(curl --fail --silent "$web_url/client-api/admin/audit?action=administration.&limit=20")
python3 -c 'import json,sys;assert len(json.load(sys.stdin)["items"])>0' <<<"$web_audit"
html=$(curl --fail --silent "$web_url/")
[[ "$html" == *"P08 · NON-PRODUCTION"* && "$html" == *"p08-administration.js"* ]] || { echo "FAIL: P08 Web shell is not activated" >&2; exit 1; }

echo "P08 enterprise administration acceptance: PASS"
