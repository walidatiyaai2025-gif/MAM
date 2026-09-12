#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5112"
work="${RUNNER_TEMP:-/tmp}/mam-p12-ocr"
rm -rf "$work" .mam-dev src/MAM.Api/.mam-dev
mkdir -p "$work"
password_key="Password"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP12OcrCi;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"

python3 - <<'PY' config/appsettings.Development.template.json "$work/ocr.json"
import json,sys
with open(sys.argv[1],encoding='utf-8') as f:d=json.load(f)
d['Jobs']['LeaseSeconds']=30
d['Jobs']['HeartbeatSeconds']=5
d['Jobs']['MaxAttempts']=5
with open(sys.argv[2],'w',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False)
PY
export MAM_CONFIG_PATH="$work/ocr.json"

tesseract --version | head -n 1
tesseract --list-langs >"$work/tesseract-langs.txt" 2>&1
grep -qx 'ara' "$work/tesseract-langs.txt"
grep -qx 'eng' "$work/tesseract-langs.txt"
pdftoppm -v 2>"$work/pdftoppm-version.txt" || true
ffmpeg -version | head -n 1
ffprobe -version | head -n 1

api_pid=""
cleanup() {
  if [[ -n "$api_pid" ]]; then
    kill "$api_pid" >/dev/null 2>&1 || true
    wait "$api_pid" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

export ASPNETCORE_URLS="$api_url"
dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api.log" 2>&1 &
api_pid=$!
ready=0
for _ in $(seq 1 80); do
  if curl --fail --silent "$api_url/health/processing" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then
  cat "$work/api.log"
  echo "FAIL: OCR acceptance API did not become processing-ready." >&2
  exit 1
fi

profiles=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/profiles")
python3 -c 'import json,sys;p={x["id"]:x for x in json.load(sys.stdin)};o=p["ocr-text-v1"];assert o["version"]==1 and o["generatesDerivative"] is True and o["outputExtension"]==".txt" and o["contentType"].startswith("text/plain") and "Arabic/English" in o["strategy"]' <<<"$profiles"

font=/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf
[[ -f "$font" ]] || { echo "FAIL: deterministic OCR test font is unavailable." >&2; exit 1; }
ffmpeg -y -hide_banner -loglevel error -f lavfi -i color=c=white:s=1400x360 \
  -vf "drawtext=fontfile=${font}:text='MAM OCR IMAGE 2026':fontcolor=black:fontsize=112:x=70:y=110" \
  -frames:v 1 "$work/ocr-image.png"

python3 - <<'PY' "$work/ocr-document.pdf"
import sys
path=sys.argv[1]
stream=b"BT /F1 48 Tf 72 500 Td (MAM OCR PDF 2026) Tj ET"
objects=[
 b"<< /Type /Catalog /Pages 2 0 R >>",
 b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
 b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
 b"<< /Length %d >>\nstream\n"%len(stream)+stream+b"\nendstream",
 b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
]
data=bytearray(b"%PDF-1.4\n")
offsets=[0]
for i,obj in enumerate(objects,1):
 offsets.append(len(data));data.extend(f"{i} 0 obj\n".encode());data.extend(obj);data.extend(b"\nendobj\n")
xref=len(data)
data.extend(f"xref\n0 {len(objects)+1}\n".encode());data.extend(b"0000000000 65535 f \n")
for off in offsets[1:]:data.extend(f"{off:010d} 00000 n \n".encode())
data.extend(f"trailer << /Size {len(objects)+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
open(path,'wb').write(data)
PY
pdftoppm -png -r 100 -f 1 -singlefile "$work/ocr-document.pdf" "$work/pdf-smoke" >/dev/null 2>"$work/pdf-smoke.log"
[[ -s "$work/pdf-smoke.png" ]] || { cat "$work/pdf-smoke.log"; echo "FAIL: generated PDF cannot be rasterized." >&2; exit 1; }

upload_file() {
  local path="$1" title="$2"
  local name size sha payload created sid finalized
  name=$(basename "$path")
  size=$(stat -c %s "$path")
  sha=$(sha256sum "$path" | awk '{print $1}')
  payload=$(python3 -c 'import json,sys;print(json.dumps({"title":sys.argv[1],"originalFileName":sys.argv[2],"expectedLength":int(sys.argv[3]),"expectedSha256":sys.argv[4]}))' "$title" "$name" "$size" "$sha")
  created=$(curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H 'Content-Type: application/json' --data "$payload" "$api_url/api/v1/uploads/sessions")
  sid=$(python3 -c 'import json,sys;print(json.load(sys.stdin)["session"]["sessionId"])' <<<"$created")
  curl --fail --silent -X PUT -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' -H "X-Chunk-SHA256: $sha" -H 'Content-Type: application/octet-stream' --data-binary @"$path" "$api_url/api/v1/uploads/sessions/$sid/chunks?offset=0" >/dev/null
  finalized=$(curl --fail --silent -X POST -H 'X-MAM-Dev-User: editor' -H 'X-MAM-Client: WindowsDesktop' "$api_url/api/v1/uploads/sessions/$sid/finalize")
  python3 -c 'import json,sys;d=json.load(sys.stdin);print(str(d["assetId"])+"|"+d["primaryObjectKey"]+"|"+d["sha256"])' <<<"$finalized"
}

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

enqueue_ocr() {
  curl --fail --silent -H 'X-MAM-Dev-User: editor' -H 'Content-Type: application/json' \
    --data '{"profileId":"ocr-text-v1"}' "$api_url/api/v1/processing/assets/$1/jobs"
}

process_and_verify() {
  local asset="$1" key="$2" expected_sha="$3" label="$4" expected_word="$5"
  local primary before job_json job_id jobs derivatives derivative_id derivative_sha
  primary=$(find_primary "$key")
  before=$(sha256sum "$primary" | awk '{print $1}')
  [[ "$before" == "$expected_sha" ]] || { echo "FAIL: $label Primary hash mismatch before OCR." >&2; exit 1; }

  job_json=$(enqueue_ocr "$asset")
  job_id=$(python3 -c 'import json,sys;d=json.load(sys.stdin);assert d["profileId"]=="ocr-text-v1" and d["state"]==0;print(d["jobId"])' <<<"$job_json")
  MAM_WORKER_ID="p12-ocr-${label}-worker" dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --processing-only --once >"$work/worker-${label}.log" 2>&1

  jobs=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/jobs?limit=100")
  python3 -c 'import json,sys;jid=sys.argv[1];j=next(x for x in json.load(sys.stdin) if x["jobId"]==jid);assert j["state"]==2 and j["completedAtUtc"] and not j["lastError"]' "$job_id" <<<"$jobs"
  derivatives=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$asset/derivatives")
  derivative_id=$(python3 -c 'import json,sys;rows=[x for x in json.load(sys.stdin) if x["profileId"]=="ocr-text-v1"];assert len(rows)==1 and rows[0]["contentType"].startswith("text/plain") and rows[0]["length"]>0;print(rows[0]["derivativeId"])' <<<"$derivatives")
  derivative_sha=$(python3 -c 'import json,sys;print(next(x for x in json.load(sys.stdin) if x["profileId"]=="ocr-text-v1")["sha256"])' <<<"$derivatives")
  curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/assets/$asset/derivatives/$derivative_id/content" -o "$work/${label}-ocr.txt"
  [[ "$(sha256sum "$work/${label}-ocr.txt" | awk '{print $1}')" == "$derivative_sha" ]] || { echo "FAIL: $label OCR derivative hash mismatch." >&2; exit 1; }
  grep -Eqi 'MAM' "$work/${label}-ocr.txt" || { cat "$work/${label}-ocr.txt"; echo "FAIL: $label OCR did not extract MAM." >&2; exit 1; }
  grep -Eqi "$expected_word" "$work/${label}-ocr.txt" || { cat "$work/${label}-ocr.txt"; echo "FAIL: $label OCR did not extract expected text." >&2; exit 1; }
  [[ "$(sha256sum "$primary" | awk '{print $1}')" == "$before" ]] || { echo "FAIL: $label Primary original changed during OCR." >&2; exit 1; }
}

image_info=$(upload_file "$work/ocr-image.png" "P12 OCR Image")
pdf_info=$(upload_file "$work/ocr-document.pdf" "P12 OCR PDF")
IFS='|' read -r image_asset image_key image_sha <<<"$image_info"
IFS='|' read -r pdf_asset pdf_key pdf_sha <<<"$pdf_info"

process_and_verify "$image_asset" "$image_key" "$image_sha" image 'OCR'
process_and_verify "$pdf_asset" "$pdf_key" "$pdf_sha" pdf 'PDF'
grep -q -- '--- Page 1 ---' "$work/pdf-ocr.txt" || { cat "$work/pdf-ocr.txt"; echo "FAIL: PDF OCR page boundary is missing." >&2; exit 1; }

audit=$(curl --fail --silent -H 'X-MAM-Dev-User: admin' "$api_url/api/v1/audit/recent?limit=100")
python3 -c 'import json,sys;rows=json.load(sys.stdin);completed=[x for x in rows if x["action"]=="processing.job.completed" and x.get("detail") and "profile=ocr-text-v1" in x["detail"]];assert len(completed)>=2' <<<"$audit"

echo "PASS: P12 OCR image/PDF Arabic+English engine profile, durable worker execution, verified text derivatives, SHA-256 integrity, audit and immutable Primary originals are verified."
