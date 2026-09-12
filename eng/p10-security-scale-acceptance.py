#!/usr/bin/env python3
import concurrent.futures
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("P10_API_URL", "http://127.0.0.1:5300").rstrip("/")
WORK = os.environ.get("P10_WORK", os.path.join(os.environ.get("RUNNER_TEMP", "/tmp"), "mam-p10"))
os.makedirs(WORK, exist_ok=True)


def request(method, path, user=None, obj=None, body=None, headers=None, timeout=20):
    data = body
    hdr = {"Accept": "application/json"}
    if user:
        hdr["X-MAM-Dev-User"] = user
    if obj is not None:
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        hdr["Content-Type"] = "application/json"
    if headers:
        hdr.update(headers)
    req = urllib.request.Request(BASE + path, data=data, headers=hdr, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            return response.status, response.read(), response.headers
    except urllib.error.HTTPError as error:
        return error.code, error.read(), error.headers


def decode(raw):
    return json.loads(raw.decode("utf-8")) if raw else None


def expect(method, path, status, user=None, obj=None, body=None, headers=None):
    code, raw, _ = request(method, path, user=user, obj=obj, body=body, headers=headers)
    if code != status:
        raise AssertionError(f"{method} {path}: expected {status}, got {code}: {raw[:500]!r}")
    return decode(raw)


# Authentication, authorization and representative unsafe-object-reference negatives.
expect("GET", "/api/v1/catalog/assets", 401)
expect("POST", "/api/v1/catalog/assets", 403, user="viewer", obj={"title": "forbidden"})
expect("POST", "/api/v1/uploads/sessions", 403, user="viewer", obj={})
expect("GET", "/api/v1/admin/overview", 403, user="editor")
expect("GET", "/api/v1/operations/summary", 403, user="viewer")
expect("GET", "/api/v1/admin/overview", 200, user="admin")
expect("GET", "/api/v1/operations/summary", 200, user="admin")
random_id = str(uuid.uuid4())
expect("GET", f"/api/v1/catalog/assets/{random_id}", 404, user="viewer")
expect("GET", f"/api/v1/uploads/sessions/{random_id}", 404, user="viewer")
expect("GET", f"/api/v1/protection/assets/{random_id}", 404, user="viewer")

# Upload/path/size/hash/quarantine negative acceptance.
zero64 = "0" * 64
expect("POST", "/api/v1/uploads/sessions", 400, user="editor", obj={
    "title": "Traversal", "originalFileName": "../escape.mp4", "expectedLength": 10, "expectedSha256": zero64})
expect("POST", "/api/v1/uploads/sessions", 400, user="editor", obj={
    "title": "Bad hash", "originalFileName": "bad.mp4", "expectedLength": 10, "expectedSha256": "abc"})
expect("POST", "/api/v1/uploads/sessions", 400, user="editor", obj={
    "title": "Zero", "originalFileName": "zero.mp4", "expectedLength": 0, "expectedSha256": zero64})
expect("POST", "/api/v1/uploads/sessions", 413, user="editor", obj={
    "title": "Too large", "originalFileName": "huge.mp4", "expectedLength": 501 * 1024 * 1024 * 1024,
    "expectedSha256": zero64})
quarantine = expect("POST", "/api/v1/uploads/sessions", 201, user="editor", obj={
    "title": "Unknown type", "originalFileName": "payload.exe", "expectedLength": 32, "expectedSha256": zero64})
assert quarantine["isQuarantined"] is True
expect("POST", f"/api/v1/uploads/sessions/{quarantine['session']['sessionId']}/finalize", 409, user="editor")

# Hard chunk limit: request body above the 16 MiB development chunk limit is rejected before offset advances.
chunk_payload = b"P10-CHUNK-" * 1_700_000
chunk_sha = hashlib.sha256(chunk_payload).hexdigest()
chunk_session = expect("POST", "/api/v1/uploads/sessions", 201, user="editor", obj={
    "title": "Chunk bound", "originalFileName": "chunk-bound.mp4", "expectedLength": len(chunk_payload),
    "expectedSha256": chunk_sha})
expect("PUT", f"/api/v1/uploads/sessions/{chunk_session['session']['sessionId']}/chunks?offset=0", 413,
       user="editor", body=chunk_payload,
       headers={"Content-Type": "application/octet-stream", "X-Chunk-SHA256": chunk_sha})

# MIME is not a trust signal. Spoofed media can reach durable upload only as bytes; inspection must fail it later.
spoof = b"not-a-real-mp4\x00P10-content-spoof"
spoof_sha = hashlib.sha256(spoof).hexdigest()
spoof_session = expect("POST", "/api/v1/uploads/sessions", 201, user="editor", obj={
    "title": "P10 content spoof", "originalFileName": "spoof.mp4", "expectedLength": len(spoof),
    "expectedSha256": spoof_sha})
spoof_sid = spoof_session["session"]["sessionId"]
expect("PUT", f"/api/v1/uploads/sessions/{spoof_sid}/chunks?offset=0", 200, user="editor", body=spoof,
       headers={"Content-Type": "video/mp4", "X-Chunk-SHA256": spoof_sha})
spoof_final = expect("POST", f"/api/v1/uploads/sessions/{spoof_sid}/finalize", 200, user="editor")
spoof_job = expect("POST", f"/api/v1/processing/assets/{spoof_final['assetId']}/jobs", 200, user="editor",
                   obj={"profileId": "inspect-v1"})

# Generated representative SQL-backed search corpus.
corpus_count = 80
for index in range(corpus_count):
    expect("POST", "/api/v1/catalog/assets", 201, user="editor", obj={"title": f"P10 Load Asset {index:03d}"})
search_path = "/api/v1/curation/search?" + urllib.parse.urlencode({
    "query": "P10 Load Asset", "page": "1", "pageSize": "100"})
search = expect("GET", search_path, 200, user="viewer")
assert search["totalCount"] >= corpus_count, search["totalCount"]

# Bounded query input is explicit backpressure rather than silent expansion.
expect("GET", "/api/v1/curation/search?page=1&pageSize=10000", 400, user="viewer")


def timed_call(path):
    started = time.perf_counter()
    code, raw, _ = request("GET", path, user="viewer", timeout=20)
    elapsed = time.perf_counter() - started
    if code != 200:
        raise AssertionError(f"load request {path} => {code}: {raw[:200]!r}")
    return elapsed


def batch(label, path, count, workers, p95_limit, total_limit):
    started = time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        samples = list(pool.map(lambda _: timed_call(path), range(count)))
    total = time.perf_counter() - started
    ordered = sorted(samples)
    p95 = ordered[max(0, int(len(ordered) * 0.95) - 1)]
    if p95 > p95_limit or total > total_limit:
        raise AssertionError(f"{label} regression: p95={p95:.3f}s total={total:.3f}s")
    return {
        "requests": count, "concurrency": workers, "p95Ms": round(p95 * 1000, 2),
        "totalMs": round(total * 1000, 2), "thresholdP95Ms": int(p95_limit * 1000),
        "thresholdTotalMs": int(total_limit * 1000)
    }


catalog_metric = batch("catalog-list", "/api/v1/catalog/assets", 120, 12, 5.0, 30.0)
search_metric = batch("curation-search", search_path, 100, 10, 5.0, 30.0)

# Representative concurrent ingest: unique sessions/files upload and promote in parallel.
ingest = []
for index in range(8):
    payload = (f"P10-CONCURRENT-{index:02d}-".encode() * 8192)[:180000]
    sha = hashlib.sha256(payload).hexdigest()
    session = expect("POST", "/api/v1/uploads/sessions", 201, user="editor", obj={
        "title": f"P10 Concurrent Ingest {index:02d}", "originalFileName": f"p10-concurrent-{index:02d}.mp4",
        "expectedLength": len(payload), "expectedSha256": sha})
    ingest.append((session["session"]["sessionId"], payload, sha))


def ingest_one(item):
    sid, payload, sha = item
    started = time.perf_counter()
    receipt = expect("PUT", f"/api/v1/uploads/sessions/{sid}/chunks?offset=0", 200, user="editor", body=payload,
                     headers={"Content-Type": "application/octet-stream", "X-Chunk-SHA256": sha})
    assert receipt["receivedLength"] == len(payload)
    final = expect("POST", f"/api/v1/uploads/sessions/{sid}/finalize", 200, user="editor")
    assert final["sha256"] == sha and final["length"] == len(payload)
    return final, time.perf_counter() - started


ingest_started = time.perf_counter()
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    completed = list(pool.map(ingest_one, ingest))
ingest_total = time.perf_counter() - ingest_started
assets = [item[0]["assetId"] for item in completed]
keys = [item[0]["primaryObjectKey"] for item in completed]
assert len(set(assets)) == len(assets) == 8
assert len(set(keys)) == len(keys) == 8
assert ingest_total < 30.0
ingest_times = sorted(item[1] for item in completed)
ingest_p95 = ingest_times[max(0, int(len(ingest_times) * 0.95) - 1)]

# Duplicate authoritative SHA remains fail-closed after concurrent promotion.
_, first_payload, first_sha = ingest[0]
expect("POST", "/api/v1/uploads/sessions", 409, user="editor", obj={
    "title": "P10 Duplicate", "originalFileName": "p10-duplicate.mp4", "expectedLength": len(first_payload),
    "expectedSha256": first_sha})

# Execute the invalid-media job through the real durable worker; it must end Failed, never Succeeded.
worker_log = os.path.join(WORK, "spoof-worker.log")
with open(worker_log, "wb") as output:
    worker = subprocess.run([
        "dotnet", "run", "--project", "src/MAM.Worker/MAM.Worker.csproj", "--configuration", "Release",
        "--no-build", "--", "--once"
    ], env={**os.environ, "MAM_WORKER_ID": "p10-content-negative"}, stdout=output, stderr=subprocess.STDOUT)
if worker.returncode not in (0, 4):
    raise AssertionError(f"unexpected invalid-media worker exit {worker.returncode}; see {worker_log}")
jobs = expect("GET", "/api/v1/processing/jobs?limit=100", 200, user="viewer")
job = next(item for item in jobs if item["jobId"] == spoof_job["jobId"])
assert job["state"] == 3, job

# Record only hosted-runner engineering measurements; production capacity remains deferred.
evidence = {
    "kind": "P10 representative CI engineering baseline",
    "productionSla": False,
    "authorizationNegative": "PASS",
    "unsafeObjectReferenceNegative": "PASS",
    "uploadValidationNegative": "PASS",
    "invalidMediaInspection": "PASS",
    "generatedSearchAssets": corpus_count,
    "catalogApiLoad": catalog_metric,
    "searchLoad": search_metric,
    "concurrentIngest": {
        "files": 8, "concurrency": 8, "p95Ms": round(ingest_p95 * 1000, 2),
        "totalMs": round(ingest_total * 1000, 2)
    },
    "deferred": [
        "production SLA/load targets", "target-site capacity certification",
        "external penetration testing", "final physical workstation/browser matrix"
    ]
}
with open(os.path.join(WORK, "p10-engineering-baseline.json"), "w", encoding="utf-8") as output:
    json.dump(evidence, output, indent=2, ensure_ascii=False)
print(json.dumps(evidence, indent=2, ensure_ascii=False))
