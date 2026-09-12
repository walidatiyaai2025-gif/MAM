param([Parameter(Mandatory=$true)][string]$ReleaseRoot)
$ErrorActionPreference='Stop'
$manifest=Get-Content -Raw (Join-Path $ReleaseRoot 'release-manifest.json') | ConvertFrom-Json
$version=[string]$manifest.version
$package=Get-ChildItem -LiteralPath $ReleaseRoot -Filter "DiwanMAM-Desktop-$version-win-x64.zip" | Select-Object -First 1
if (-not $package) { throw 'Desktop release package missing.' }
$work=Join-Path $env:RUNNER_TEMP 'mam-p11-windows'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
$control=Join-Path $work 'control'; $install=Join-Path $work 'install'; $primary=Join-Path $work 'primary'; $backup=Join-Path $work 'backup'
New-Item -ItemType Directory -Force -Path $control,$primary,$backup | Out-Null
Set-Content -LiteralPath (Join-Path $primary 'authoritative-primary.marker') -Value 'preserve-primary' -NoNewline
Set-Content -LiteralPath (Join-Path $backup 'authoritative-backup.marker') -Value 'preserve-backup' -NoNewline
Expand-Archive -LiteralPath $package.FullName -DestinationPath $control -Force
$installer=Join-Path $control 'Install-MamDesktop.ps1'; $uninstaller=Join-Path $control 'Uninstall-MamDesktop.ps1'; $signer=Join-Path $control 'Sign-MamDesktop.ps1'
foreach($f in @($installer,$uninstaller,$signer)){ if(-not(Test-Path $f)){throw "Desktop control missing: $f"} }

& $installer -PackagePath $package.FullName -InstallRoot $install -PrimaryRoot $primary -BackupRoot $backup
if (-not (Test-Path (Join-Path $install 'MAM.Desktop.exe'))) { throw 'Installed Desktop executable missing.' }
$installedMeta=Get-Content -Raw (Join-Path $install '.mam-install.json') | ConvertFrom-Json
if ([string]$installedMeta.version -ne $version) { throw 'Installed Desktop version does not match release manifest.' }

# Upgrade in place from the generated candidate must leave authoritative roots untouched.
& $installer -PackagePath $package.FullName -InstallRoot $install -PrimaryRoot $primary -BackupRoot $backup
if ((Get-Content -Raw (Join-Path $primary 'authoritative-primary.marker')) -ne 'preserve-primary') { throw 'Desktop upgrade changed Primary data.' }
if ((Get-Content -Raw (Join-Path $backup 'authoritative-backup.marker')) -ne 'preserve-backup') { throw 'Desktop upgrade changed Backup data.' }

# Signing must fail closed when owner-provided signing identity is absent.
$signFailed=$false
try { & $signer -AppRoot $install -CertificateThumbprint '' } catch { $signFailed=$true }
if (-not $signFailed) { throw 'Signing workflow did not fail closed without a real certificate.' }

# Overlapping application/storage roots are forbidden.
$overlapRejected=$false
try { & $installer -PackagePath $package.FullName -InstallRoot (Join-Path $primary 'application') -PrimaryRoot $primary -BackupRoot $backup } catch { $overlapRejected=$true }
if (-not $overlapRejected) { throw 'Installer accepted application files inside Primary Storage.' }

& $uninstaller -InstallRoot $install -PrimaryRoot $primary -BackupRoot $backup
if (Test-Path $install) { throw 'Desktop application directory still exists after uninstall.' }
if ((Get-Content -Raw (Join-Path $primary 'authoritative-primary.marker')) -ne 'preserve-primary') { throw 'Uninstall changed Primary data.' }
if ((Get-Content -Raw (Join-Path $backup 'authoritative-backup.marker')) -ne 'preserve-backup') { throw 'Uninstall changed Backup data.' }

@{status='PASS';version=$version;cleanInstall=$true;upgradePreserved=$true;uninstallPreserved=$true;signingFailClosed=$true;storageOverlapRejected=$true} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $work 'p11-windows-install-evidence.json')
Write-Output 'PASS: P11 Desktop clean install, upgrade, fail-closed signing and uninstall preservation acceptance.'
