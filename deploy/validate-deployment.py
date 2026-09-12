#!/usr/bin/env python3
import argparse, hashlib, json, pathlib, re, sys

p=argparse.ArgumentParser()
p.add_argument('--release-dir',required=True)
p.add_argument('--config',required=True)
p.add_argument('--template',action='store_true')
p.add_argument('--materialized',action='store_true')
p.add_argument('--install-root')
p.add_argument('--primary-root')
p.add_argument('--backup-root')
a=p.parse_args()
if a.template == a.materialized:
    raise SystemExit('choose exactly one of --template or --materialized')
release=pathlib.Path(a.release_dir).resolve(); config_path=pathlib.Path(a.config).resolve()
if not release.is_dir(): raise SystemExit('release directory missing')
if not config_path.is_file(): raise SystemExit('configuration file missing')

manifest_path=release/'release-manifest.json'; sums_path=release/'SHA256SUMS.txt'
if not manifest_path.is_file() or not sums_path.is_file(): raise SystemExit('release manifest/checksum file missing')
manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
if manifest.get('phase')!='P11' or not manifest.get('version') or not manifest.get('sourceCommit'): raise SystemExit('invalid P11 release manifest')
required_roles=('Api','Web','Worker','Desktop','SqlMigrations','ProductionConfig')
files={row['file']:row for row in manifest.get('artifacts',[])}
for role in required_roles:
    if not any(f'-{role}-' in name for name in files): raise SystemExit(f'missing release package role: {role}')

def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()
for name,row in files.items():
    q=release/name
    if not q.is_file(): raise SystemExit(f'missing artifact: {name}')
    if sha(q)!=row['sha256']: raise SystemExit(f'manifest hash mismatch: {name}')

for line in sums_path.read_text(encoding='utf-8').splitlines():
    if not line.strip(): continue
    digest,name=line.split(None,1); name=name.strip()
    q=release/name
    if not q.is_file() or sha(q)!=digest: raise SystemExit(f'SHA256SUMS mismatch: {name}')

cfg=json.loads(config_path.read_text(encoding='utf-8'))
flat=json.dumps(cfg,ensure_ascii=False)
if a.template:
    for token in ('REPLACE-WITH-PRODUCTION-FQDN','REPLACE-WITH-SECRET-REFERENCE','REPLACE-WITH-PRIMARY-STORAGE-ROOT','REPLACE-WITH-BACKUP-STORAGE-ROOT'):
        if token not in flat: raise SystemExit(f'production template lost required owner/site placeholder: {token}')
else:
    if 'REPLACE-WITH-' in flat: raise SystemExit('materialized production configuration still contains owner/site placeholders')
    url=cfg.get('Server',{}).get('PublicBaseUrl','')
    if not re.match(r'^https://[^/\s]+',url): raise SystemExit('production PublicBaseUrl must be HTTPS')

sensitive=re.compile(r'(password|clientsecret|apikey|privatekey|token)$',re.I)
def walk(obj,path=''):
    if isinstance(obj,dict):
        for k,v in obj.items():
            key=f'{path}.{k}' if path else k
            if sensitive.search(k) and isinstance(v,str) and v and 'REPLACE-WITH' not in v and 'Ref' not in k:
                raise SystemExit(f'plaintext-sensitive value detected in config key {key}')
            walk(v,key)
    elif isinstance(obj,list):
        for i,v in enumerate(obj): walk(v,f'{path}[{i}]')
walk(cfg)

def canonical(x): return pathlib.Path(x).expanduser().resolve()
def overlap(x,y):
    x=canonical(x); y=canonical(y)
    return x==y or x in y.parents or y in x.parents
if a.install_root and a.primary_root and overlap(a.install_root,a.primary_root): raise SystemExit('InstallRoot overlaps PrimaryRoot')
if a.install_root and a.backup_root and overlap(a.install_root,a.backup_root): raise SystemExit('InstallRoot overlaps BackupRoot')
if a.primary_root and a.backup_root and overlap(a.primary_root,a.backup_root): raise SystemExit('PrimaryRoot overlaps BackupRoot')
print(json.dumps({'status':'ok','version':manifest['version'],'sourceCommit':manifest['sourceCommit'],'artifacts':len(files)},indent=2))
