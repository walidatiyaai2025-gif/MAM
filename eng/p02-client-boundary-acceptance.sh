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
  'src/MAM.Desktop/MainWindow.xaml.cs:MamCatalogApiClient' \
  'src/MAM.Web/Program.cs:MamCatalogApiClient'; do
  file="${required%%:*}"
  token="${required#*:}"
  grep -q "$token" "$file" || { echo "FAIL: $file does not use the Central API client contract." >&2; fail=1; }
done

[[ "$fail" == "0" ]] || exit 1

echo "PASS: Desktop and Web depend on the Central API client contract and contain no direct SQL Server package, connection, or credential path."
