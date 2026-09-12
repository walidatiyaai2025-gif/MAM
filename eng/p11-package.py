#!/usr/bin/env python3
import hashlib, json, os, pathlib, sys, zipfile

if len(sys.argv) != 5:
    raise SystemExit("usage: p11-package.py <staging> <release> <version> <commit>")
staging = pathlib.Path(sys.argv[1]).resolve()
release = pathlib.Path(sys.argv[2]).resolve()
version, commit = sys.argv[3], sys.argv[4]
release.mkdir(parents=True, exist_ok=True)

packages = {
    f"DiwanMAM-Api-{version}.zip": "api",
    f"DiwanMAM-Web-{version}.zip": "web",
    f"DiwanMAM-Worker-{version}.zip": "worker",
    f"DiwanMAM-Desktop-{version}-win-x64.zip": "desktop",
    f"DiwanMAM-SqlMigrations-{version}.zip": "sql",
    f"DiwanMAM-ProductionConfig-{version}.zip": "config",
}

EPOCH = (1980, 1, 1, 0, 0, 0)

def make_zip(src: pathlib.Path, dst: pathlib.Path):
    if not src.is_dir():
        raise SystemExit(f"missing staging directory: {src}")
    with zipfile.ZipFile(dst, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted((p for p in src.rglob("*") if p.is_file()), key=lambda x: x.relative_to(src).as_posix()):
            rel = p.relative_to(src).as_posix()
            info = zipfile.ZipInfo(rel, EPOCH)
            info.compress_type = zipfile.ZIP_DEFLATED
            mode = 0o755 if os.access(p, os.X_OK) or p.suffix.lower() in {".sh", ".ps1"} else 0o644
            info.external_attr = mode << 16
            info.create_system = 3
            z.writestr(info, p.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

def sha(path: pathlib.Path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

for name, folder in packages.items():
    make_zip(staging / folder, release / name)

artifact_rows = []
for name in sorted(packages):
    p = release / name
    artifact_rows.append({"file": name, "bytes": p.stat().st_size, "sha256": sha(p)})

manifest = {
    "product": "Diwan Al Amiri Media Asset Management",
    "phase": "P11",
    "version": version,
    "sourceCommit": commit,
    "buildIdentity": f"{version}+{commit[:12]}",
    "desktopSigningStatus": "UNSIGNED_ENGINEERING_CANDIDATE",
    "productionSigningEvidence": "DEFERRED_TO_P12_OWNER_LAST",
    "artifacts": artifact_rows,
}
manifest_path = release / "release-manifest.json"
manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

checksum_targets = sorted([release / row["file"] for row in artifact_rows] + [manifest_path] + ([release / "RELEASE_NOTES.md"] if (release / "RELEASE_NOTES.md").exists() else []), key=lambda p: p.name)
(release / "SHA256SUMS.txt").write_text("".join(f"{sha(p)}  {p.name}\n" for p in checksum_targets), encoding="utf-8")
print(json.dumps(manifest, indent=2, ensure_ascii=False))
