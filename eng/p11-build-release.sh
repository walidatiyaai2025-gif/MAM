#!/usr/bin/env bash
set -euo pipefail

release_root="${1:-${RUNNER_TEMP:-/tmp}/mam-p11/release}"
staging="${RUNNER_TEMP:-/tmp}/mam-p11/staging"
version=$(python3 - <<'PY'
import re
s=open('Directory.Build.props',encoding='utf-8').read()
m=re.search(r'<Version>([^<]+)</Version>',s)
assert m, 'Version missing'
print(m.group(1))
PY
)
commit="${MAM_COMMIT_SHA:-$(git rev-parse HEAD)}"

rm -rf "$staging" "$release_root"
mkdir -p "$staging"/{api,web,worker,desktop/app,sql/tool,sql/migrations,config} "$release_root"

common=(-c Release -p:SourceRevisionId="$commit")
dotnet publish src/MAM.Api/MAM.Api.csproj "${common[@]}" -o "$staging/api"
dotnet publish src/MAM.Web/MAM.Web.csproj "${common[@]}" -o "$staging/web"
dotnet publish src/MAM.Worker/MAM.Worker.csproj "${common[@]}" -o "$staging/worker"
dotnet publish tools/MAM.Deployment/MAM.Deployment.csproj "${common[@]}" -o "$staging/sql/tool"
dotnet publish src/MAM.Desktop/MAM.Desktop.csproj "${common[@]}" -p:EnableWindowsTargeting=true -r win-x64 --self-contained false -o "$staging/desktop/app"

cp database/migrations/*.sql "$staging/sql/migrations/"
cp config/appsettings.Production.template.json "$staging/config/appsettings.Production.template.json"
cp deploy/validate-deployment.py "$staging/config/validate-deployment.py"
cp deploy/desktop/Install-MamDesktop.ps1 deploy/desktop/Uninstall-MamDesktop.ps1 deploy/desktop/Sign-MamDesktop.ps1 "$staging/desktop/"
printf '{"version":"%s","sourceCommit":"%s","signingStatus":"UNSIGNED_ENGINEERING_CANDIDATE"}\n' "$version" "$commit" > "$staging/desktop/release-metadata.json"
cp docs/releases/P11_RELEASE_NOTES.md "$release_root/RELEASE_NOTES.md"

python3 eng/p11-package.py "$staging" "$release_root" "$version" "$commit"
python3 deploy/validate-deployment.py --release-dir "$release_root" --config config/appsettings.Production.template.json --template

echo "P11 release candidate built: $release_root"
