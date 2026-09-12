param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$PrimaryRoot,
  [Parameter(Mandatory=$true)][string]$BackupRoot
)
$ErrorActionPreference = 'Stop'
function Canonical([string]$Path) { [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) }
function Assert-Separate([string]$A,[string]$B,[string]$Label) {
  $a2=(Canonical $A) + [IO.Path]::DirectorySeparatorChar
  $b2=(Canonical $B) + [IO.Path]::DirectorySeparatorChar
  if ($a2.StartsWith($b2,[StringComparison]::OrdinalIgnoreCase) -or $b2.StartsWith($a2,[StringComparison]::OrdinalIgnoreCase)) { throw "$Label paths overlap; refusing uninstall." }
}
$install=Canonical $InstallRoot; $primary=Canonical $PrimaryRoot; $backup=Canonical $BackupRoot
Assert-Separate $install $primary 'Install/Primary'
Assert-Separate $install $backup 'Install/Backup'
Assert-Separate $primary $backup 'Primary/Backup'
if (Test-Path $install) { Remove-Item -LiteralPath $install -Recurse -Force }
Write-Output "Removed Diwan MAM Desktop application files only. Authoritative Primary/Backup roots were not modified."
