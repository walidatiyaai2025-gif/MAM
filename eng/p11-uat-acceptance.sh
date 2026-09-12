#!/usr/bin/env bash
set -euo pipefail
bash eng/p10-client-ui-boundary-acceptance.sh
python3 -m json.tool config/appsettings.Production.template.json >/dev/null

doc=docs/uat/P11_UAT.md
notes=docs/releases/P11_RELEASE_NOTES.md
for term in 'Arabic' 'English' 'RTL' 'LTR' 'responsive' 'loading' 'empty' 'error' 'degraded' 'upload' 'search' 'administration' 'reports' 'Windows-only'; do
  grep -qi "$term" "$doc" || { echo "FAIL: P11 UAT is missing coverage term: $term" >&2; exit 1; }
done
grep -q 'UNSIGNED_ENGINEERING_CANDIDATE' "$notes" || { echo 'FAIL: release notes must state engineering signing status.' >&2; exit 1; }
grep -q 'DEFERRED_TO_P12' "$notes" || { echo 'FAIL: release notes must state owner/site deferral.' >&2; exit 1; }
grep -q 'ConnectionStringSecretRef' config/appsettings.Production.template.json || { echo 'FAIL: production config lost secret-reference boundary.' >&2; exit 1; }
python3 - <<'PY'
import json,re
x=json.load(open('config/appsettings.Production.template.json',encoding='utf-8'))
sensitive=re.compile(r'(password|clientsecret|apikey|privatekey|token)$',re.I)
def walk(v,path=''):
    if isinstance(v,dict):
        for k,z in v.items():
            p=f'{path}.{k}' if path else k
            if sensitive.search(k) and isinstance(z,str) and z and 'REPLACE-WITH' not in z and not k.lower().endswith('ref'):
                raise SystemExit(f'plaintext-sensitive template value: {p}')
            walk(z,p)
    elif isinstance(v,list):
        for i,z in enumerate(v): walk(z,f'{path}[{i}]')
walk(x)
PY
echo 'PASS: P11 bilingual/responsive/error-state UAT contract and Central-API/platform boundaries are explicit and executable.'
