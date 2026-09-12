param(
  [Parameter(Mandatory=$true)][string]$AppRoot,
  [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
  [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) { throw 'A real code-signing certificate thumbprint is required. Signing is not simulated.' }
$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) { throw 'signtool.exe is required for Desktop production signing.' }
$targets = Get-ChildItem -LiteralPath $AppRoot -Recurse -File | Where-Object { $_.Extension -in @('.exe','.dll') }
if (-not $targets) { throw 'No Desktop binaries were found to sign.' }
foreach ($target in $targets) {
  & $signtool.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $target.FullName
  if ($LASTEXITCODE -ne 0) { throw "Signing failed for $($target.FullName)." }
}
Write-Output "Signed $($targets.Count) Desktop binaries with real certificate evidence."
