#!/usr/bin/env bash
set -euo pipefail

fail=0
for path in src/MAM.Desktop src/MAM.Web; do
  if grep -R -nE 'Microsoft\.Data\.SqlClient|\bSqlConnection\b|MAM_SECRET_DATABASE|ConnectionStringSecretRef' "$path" --exclude-dir=bin --exclude-dir=obj; then
    echo "FAIL: direct database implementation/credential reference found in $path" >&2
    fail=1
  fi
done

if grep -R -nE '<ProjectReference Include="\.\./MAM\.Infrastructure|PackageReference Include="Microsoft\.Data\.SqlClient' src/MAM.Desktop/*.csproj src/MAM.Web/*.csproj; then
  echo "FAIL: client project has a direct Infrastructure/SQL dependency." >&2
  fail=1
fi

for required in \
  'src/MAM.Desktop/MainWindow.P02.cs:MamCatalogApiClient' \
  'src/MAM.Desktop/MainWindow.P02.cs:MAM_API_BASE_URL' \
  'src/MAM.Desktop/MainWindow.P02.cs:CreateAssetAsync' \
  'src/MAM.Web/Program.cs:MamCatalogApiClient' \
  'src/MAM.Web/Program.cs:MAM_API_BASE_URL' \
  'src/MAM.Web/wwwroot/app.js:/client-api/catalog/assets'; do
  file="${required%%:*}"
  token="${required#*:}"
  grep -q "$token" "$file" || { echo "FAIL: $file is missing P02 Central API integration token: $token" >&2; fail=1; }
done

for state in 'Loading' 'Empty' 'API error' 'Permission denied' 'Degraded'; do
  grep -q "$state" src/MAM.Desktop/MainWindow.P02.cs src/MAM.Desktop/MainWindow.xaml.cs || { echo "FAIL: Desktop connected workflow missing state: $state" >&2; fail=1; }
  grep -q "$state" src/MAM.Web/wwwroot/app.js || { echo "FAIL: Web connected workflow missing state: $state" >&2; fail=1; }
done

[[ "$fail" == "0" ]] || exit 1

echo "PASS: Desktop and Web use the Central API for catalog reads/writes, preserve connected failure states, and contain no direct SQL Server package, connection, or credential path."
