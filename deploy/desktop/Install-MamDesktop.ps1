param(
  [Parameter(Mandatory=$true)][string]$PackagePath,
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$PrimaryRoot,
  [Parameter(Mandatory=$true)][string]$BackupRoot
)
$ErrorActionPreference = 'Stop'

function Canonical([string]$Path) {
  return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
}
function Assert-Separate([string]$A,[string]$B,[string]$Label) {
  $a2=(Canonical $A) + [IO.Path]::DirectorySeparatorChar
  $b2=(Canonical $B) + [IO.Path]::DirectorySeparatorChar
  if ($a2.StartsWith($b2,[StringComparison]::OrdinalIgnoreCase) -or $b2.StartsWith($a2,[StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label paths overlap; refusing installation."
  }
}

$package = Canonical $PackagePath
$install = Canonical $InstallRoot
$primary = Canonical $PrimaryRoot
$backup = Canonical $BackupRoot
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Desktop package was not found: $package" }
Assert-Separate $install $primary 'Install/Primary'
Assert-Separate $install $backup 'Install/Backup'
Assert-Separate $primary $backup 'Primary/Backup'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('mam-install-' + [Guid]::NewGuid().ToString('N'))
try {
  Expand-Archive -LiteralPath $package -DestinationPath $temp -Force
  $app = Join-Path $temp 'app'
  $meta = Join-Path $temp 'release-metadata.json'
  if (-not (Test-Path $app -PathType Container)) { throw 'Desktop package is missing app/.' }
  if (-not (Test-Path $meta -PathType Leaf)) { throw 'Desktop package is missing release-metadata.json.' }
  $metadata = Get-Content -Raw -LiteralPath $meta | ConvertFrom-Json
  if ([string]::IsNullOrWhiteSpace([string]$metadata.version)) { throw 'Desktop release version is missing.' }

  $parent = Split-Path -Parent $install
  New-Item -ItemType Directory -Force -Path $parent | Out-Null
  $next = "$install.next"
  Remove-Item -LiteralPath $next -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $next | Out-Null
  Copy-Item -Path (Join-Path $app '*') -Destination $next -Recurse -Force
  Copy-Item -LiteralPath $meta -Destination (Join-Path $next '.mam-install.json') -Force

  $previous = "$install.previous"
  Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction SilentlyContinue
  if (Test-Path $install) { Move-Item -LiteralPath $install -Destination $previous }
  try { Move-Item -LiteralPath $next -Destination $install }
  catch {
    if (Test-Path $previous) { Move-Item -LiteralPath $previous -Destination $install }
    throw
  }
  Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction SilentlyContinue
  Write-Output "Installed Diwan MAM Desktop $($metadata.version) to $install"
}
finally {
  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
