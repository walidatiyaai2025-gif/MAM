#!/usr/bin/env bash
set -euo pipefail

: "${MAM_SQL_TEST_PASSWORD:?MAM_SQL_TEST_PASSWORD is required}"
api_url="http://127.0.0.1:5300"
work="${RUNNER_TEMP:-/tmp}/mam-p10"
rm -rf "$work"
mkdir -p "$work"
rm -rf .mam-dev/primary .mam-dev/backup src/MAM.Api/.mam-dev/primary src/MAM.Api/.mam-dev/backup

password_key="Password"
export MAM_CONFIG_PATH="$PWD/config/appsettings.Development.template.json"
export MAM_SECRET_DATABASE="Server=127.0.0.1,14333;Initial Catalog=MamP02Ci;User ID=sa;${password_key}=${MAM_SQL_TEST_PASSWORD};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
export MAM_APPLY_MIGRATIONS=false
export MAM_MIGRATIONS_PATH="$PWD/database/migrations"
export MAM_STORAGE_BASE_PATH="$PWD"
export ASPNETCORE_URLS="$api_url"

dotnet run --project src/MAM.Api/MAM.Api.csproj --configuration Release --no-build >"$work/api.log" 2>&1 &
api_pid=$!
cleanup() {
  kill "$api_pid" >/dev/null 2>&1 || true
  wait "$api_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

ready=0
for _ in $(seq 1 100); do
  if curl --fail --silent "$api_url/health/ready" >/dev/null 2>&1 && curl --fail --silent "$api_url/health/curation" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep .5
done
if [[ "$ready" != "1" ]]; then
  cat "$work/api.log"
  echo "FAIL: P10 SQL-backed API did not become ready." >&2
  exit 1
fi

python3 - "$api_url" "$work" <<'PY'
import concurrent.futures
import hashlib
import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

base=sys.argv[1].rstrip('/')
work=sys.argv[2]

def request(method,path,user=None,obj=None,body=None,headers=None,timeout=20):
    data=body
    hdr={'Accept':'application/json'}
    if user: hdr['X-MAM-Dev-User']=user
    if obj is not None:
        data=json.dumps(obj,ensure_ascii=False).encode('utf-8')
        hdr['Content-Type']='application/json'
    if headers: hdr.update(headers)
    req=urllib.request.Request(base+path,data=data,headers=hdr,method=method)
    try:
        with urllib.request.urlopen(req,timeout=timeout) as r:
            raw=r.read()
            return r.status,raw,r.headers
    except urllib.error.HTTPError as e:
        return e.code,e.read(),e.headers

def as_json(raw):
    return json.loads(raw.decode('utf-8')) if raw else None

def expect(method,path,status,user=None,obj=None,body=None,headers=None):
    code,raw,_=request(method,path,user=user,obj=obj,body=body,headers=headers)
    if code != status:
        raise AssertionError(f'{method} {path}: expected {status}, got {code}: {raw[:500]!r}')
    return as_json(raw) if raw else None

# Authentication, authorization and representative unsafe-object-reference negatives.
expect('GET','/api/v1/catalog/assets',401)
expect('POST','/api/v1/catalog/assets',403,user='viewer',obj={'title':'forbidden'})
expect('POST','/api/v1/uploads/sessions',403,user='viewer',obj={})
expect('GET','/api/v1/admin/overview',403,user='editor')
expect('GET','/api/v1/operations/summary',403,user='viewer')
expect('GET','/api/v1/admin/overview',200,user='admin')
expect('GET','/api/v1/operations/summary',200,user='admin')
random_id=str(uuid.uuid4())
expect('GET',f'/api/v1/catalog/assets/{random_id}',404,user='viewer')
expect('GET',f'/api/v1/uploads/sessions/{random_id}',404,user='viewer')
expect('GET',f'/api/v1/protection/assets/{random_id}',404,user='viewer')

zero64='0'*64
# Upload/path/size/hash/quarantine negative acceptance.
expect('POST','/api/v1/uploads/sessions',400,user='editor',obj={
    'title':'Traversal','originalFileName':'../escape.mp4','expectedLength':10,'expectedSha256':zero64})
expect('POST','/api/v1/uploads/sessions',400,user='editor',obj={
    'title':'Bad hash','originalFileName':'bad.mp4','expectedLength':10,'expectedSha256':'abc'})
expect('POST','/api/v1/uploads/sessions',400,user='editor',obj={
    'title':'Zero','originalFileName':'zero.mp4','expectedLength':0,'expectedSha256':zero64})
expect('POST','/api/v1/uploads/sessions',413,user='editor',obj={
    'title':'Too large','originalFileName':'huge.mp4','expectedLength':501*1024*1024*1024,'expectedSha256':zero64})
q=expect('POST','/api/v1/uploads/sessions',201,user='editor',obj={
    'title':'Unknown type','originalFileName':'payload.exe','expectedLength':32,'expectedSha256':zero64})
assert q['isQuarantined'] is True
expect('POST',f"/api/v1/uploads/sessions/{q['session']['sessionId']}/finalize",409,user='editor')

# Hard chunk bound without allocating a declared 500 GiB file.
chunk_payload=b'P10-CHUNK-'*(1700000)  # >16 MiB
chunk_sha=hashlib.sha256(chunk_payload).hexdigest()
s=expect('POST','/api/v1/uploads/sessions',201,user='editor',obj={
    'title':'Chunk bound','originalFileName':'chunk-bound.mp4','expectedLength':len(chunk_payload),'expectedSha256':chunk_sha})
expect('PUT',f"/api/v1/uploads/sessions/{s['session']['sessionId']}/chunks?offset=0",413,user='editor',body=chunk_payload,
       headers={'Content-Type':'application/octet-stream','X-Chunk-SHA256':chunk_sha})

# A MIME header never becomes trust. Bytes named as media may upload, but invalid media must not be blessed by processing.
spoof=b'not-a-real-mp4\x00P10-content-spoof'
spoof_sha=hashlib.sha256(spoof).hexdigest()
sp=expect('POST','/api/v1/uploads/sessions',201,user='editor',obj={
    'title':'P10 content spoof','originalFileName':'spoof.mp4','expectedLength':len(spoof),'expectedSha256':spoof_sha})
sid=sp['session']['sessionId']
expect('PUT',f'/api/v1/uploads/sessions/{sid}/chunks?offset=0',200,user='editor',body=spoof,
       headers={'Content-Type':'video/mp4','X-Chunk-SHA256':spoof_sha})
fin=expect('POST',f'/api/v1/uploads/sessions/{sid}/finalize',200,user='editor')
spoof_asset=fin['assetId']
job=expect('POST',f'/api/v1/processing/assets/{spoof_asset}/jobs',200,user='editor',obj={'profileId':'inspect-v1'})
with open(os.path.join(work,'spoof-job.json'),'w',encoding='utf-8') as f: json.dump(job,f)

# Generated representative SQL-backed search corpus.
count=80
created=[]
for i in range(count):
    item=expect('POST','/api/v1/catalog/assets',201,user='editor',obj={'title':f'P10 Load Asset {i:03d}'})
    created.append(item['id'])
search_path='/api/v1/curation/search?'+urllib.parse.urlencode({'query':'P10 Load Asset','page':'1','pageSize':'100'})
search=expect('GET',search_path,200,user='viewer')
assert search['totalCount'] >= count, search['totalCount']

# Bounded query/bulk input is explicit backpressure rather than silent expansion.
expect('GET','/api/v1/curation/search?page=1&pageSize=10000',400,user='viewer')

# Measured hosted-runner API/SQL search baselines. Thresholds are regression tripwires, not production SLAs.
def timed_call(path,user='viewer'):
    start=time.perf_counter()
    code,raw,_=request('GET',path,user=user,timeout=20)
    elapsed=time.perf_counter()-start
    if code != 200: raise AssertionError(f'load request {path} => {code}: {raw[:200]!r}')
    return elapsed

def batch(label,path,n,workers,p95_limit,total_limit):
    start=time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        samples=list(pool.map(lambda _: timed_call(path),range(n)))
    total=time.perf_counter()-start
    ordered=sorted(samples)
    p95=ordered[max(0,int(len(ordered)*0.95)-1)]
    if p95 > p95_limit or total > total_limit:
        raise AssertionError(f'{label} regression: p95={p95:.3f}s total={total:.3f}s')
    return {'requests':n,'concurrency':workers,'p95Ms':round(p95*1000,2),'totalMs':round(total*1000,2),
            'thresholdP95Ms':int(p95_limit*1000),'thresholdTotalMs':int(total_limit*1000)}

catalog_metric=batch('catalog-list','/api/v1/catalog/assets',120,12,5.0,30.0)
search_metric=batch('curation-search',search_path,100,10,5.0,30.0)

# Representative concurrent ingest: unique sessions/files are uploaded and finalized in parallel.
ingest=[]
for i in range(8):
    payload=(f'P10-CONCURRENT-{i:02d}-'.encode()*8192)[:180000]
    sha=hashlib.sha256(payload).hexdigest()
    session=expect('POST','/api/v1/uploads/sessions',201,user='editor',obj={
        'title':f'P10 Concurrent Ingest {i:02d}','originalFileName':f'p10-concurrent-{i:02d}.mp4',
        'expectedLength':len(payload),'expectedSha256':sha})
    ingest.append((session['session']['sessionId'],payload,sha))

def ingest_one(item):
    sid,payload,sha=item
    started=time.perf_counter()
    put=expect('PUT',f'/api/v1/uploads/sessions/{sid}/chunks?offset=0',200,user='editor',body=payload,
               headers={'Content-Type':'application/octet-stream','X-Chunk-SHA256':sha})
    assert put['receivedLength']==len(payload)
    done=expect('POST',f'/api/v1/uploads/sessions/{sid}/finalize',200,user='editor')
    assert done['sha256']==sha and done['length']==len(payload)
    return done,time.perf_counter()-started

ingest_start=time.perf_counter()
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    completed=list(pool.map(ingest_one,ingest))
ingest_total=time.perf_counter()-ingest_start
assets=[x[0]['assetId'] for x in completed]
keys=[x[0]['primaryObjectKey'] for x in completed]
assert len(set(assets))==len(assets)==8
assert len(set(keys))==len(keys)==8
assert ingest_total < 30.0

ingest_times=sorted(x[1] for x in completed)
ingest_p95=ingest_times[max(0,int(len(ingest_times)*0.95)-1)]

# Duplicate authoritative SHA remains fail-closed after concurrent promotion.
_,payload,sha=ingest[0]
expect('POST','/api/v1/uploads/sessions',409,user='editor',obj={
    'title':'P10 Duplicate','originalFileName':'p10-duplicate.mp4','expectedLength':len(payload),'expectedSha256':sha})

evidence={
  'kind':'P10 representative CI engineering baseline',
  'productionSla':False,
  'authorizationNegative':'PASS',
  'unsafeObjectReferenceNegative':'PASS',
  'uploadValidationNegative':'PASS',
  'generatedSearchAssets':count,
  'catalogApiLoad':catalog_metric,
  'searchLoad':search_metric,
  'concurrentIngest':{'files':8,'concurrency':8,'p95Ms':round(ingest_p95*1000,2),'totalMs':round(ingest_total*1000,2)},
  'deferred':['production SLA/load targets','target-site capacity certification','external penetration testing','final physical workstation/browser matrix']
}
with open(os.path.join(work,'p10-engineering-baseline.json'),'w',encoding='utf-8') as f:
    json.dump(evidence,f,indent=2,ensure_ascii=False)
print(json.dumps(evidence,indent=2,ensure_ascii=False))
PY

# Finish the content-spoof negative through the real durable worker. Invalid media may never become a successful inspection.
spoof_job=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["jobId"])' "$work/spoof-job.json")
set +e
MAM_WORKER_ID=p10-content-negative dotnet run --project src/MAM.Worker/MAM.Worker.csproj --configuration Release --no-build -- --once >"$work/spoof-worker.log" 2>&1
worker_code=$?
set -e
[[ "$worker_code" == "4" ]] || { cat "$work/spoof-worker.log"; echo "FAIL: invalid media inspection expected worker failure exit 4, got $worker_code" >&2; exit 1; }
jobs=$(curl --fail --silent -H 'X-MAM-Dev-User: viewer' "$api_url/api/v1/processing/jobs?limit=100")
python3 - "$spoof_job" <<<"$jobs" <<'PY'
import json,sys
jid=sys.argv[1]
rows=json.load(sys.stdin)
job=next(x for x in rows if x['jobId']==jid)
assert job['state'] != 2, job
PY

echo "P10 security/scale/concurrent-ingest acceptance passed."