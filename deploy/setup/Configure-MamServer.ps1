param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][ValidateSet('Production','UAT')][string]$EnvironmentName,
  [Parameter(Mandatory=$true)][string]$PublicHost,
  [Parameter(Mandatory=$true)][int]$ApiPort,
  [Parameter(Mandatory=$true)][int]$WebPort,
  [Parameter(Mandatory=$true)][string]$SqlSecretInputPath,
  [Parameter(Mandatory=$true)][string]$PrimaryRoot,
  [Parameter(Mandatory=$true)][string]$BackupRoot,
  [Parameter(Mandatory=$true)][string]$BackupPolicyId,
  [Parameter(Mandatory=$true)][ValidateSet('ActiveDirectory','OIDC','Local')][string]$AuthMode,
  [Parameter(Mandatory=$true)][int]$RetentionDays,
  [Parameter(Mandatory=$true)][int]$AuditRetentionDays,
  [Parameter(Mandatory=$true)][string]$AuditReadPolicy,
  [Parameter(Mandatory=$true)][ValidateSet('System','Custom')][string]$ServiceMode,
  [string]$ServiceUser = '',
  [string]$ServicePasswordInputPath = '',
  [string]$TlsPfxPath = '',
  [string]$TlsPasswordInputPath = '',
  [int]$ApplyMigrations = 1,
  [int]$OpenFirewall = 1,
  [int]$StartServices = 1
)
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not guarantee that System.Security is loaded before
# ProtectedData is first referenced. Load it explicitly so DPAPI LocalMachine
# protection is deterministic on clean Windows hosts and CI runners.
Add-Type -AssemblyName System.Security -ErrorAction Stop

function Canonical([string]$Path) { [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) }
function Assert-Separate([string]$A,[string]$B,[string]$Label) {
  if ($A.StartsWith('\\') -or $B.StartsWith('\\')) { if ($A.TrimEnd('\') -ieq $B.TrimEnd('\')) { throw "$Label targets must be distinct." }; return }
  $a2=(Canonical $A)+[IO.Path]::DirectorySeparatorChar; $b2=(Canonical $B)+[IO.Path]::DirectorySeparatorChar
  if ($a2.StartsWith($b2,[StringComparison]::OrdinalIgnoreCase) -or $b2.StartsWith($a2,[StringComparison]::OrdinalIgnoreCase)) { throw "$Label paths overlap." }
}
function Read-Secret([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Secret input file not found: $Path" }
  return (Get-Content -Raw -LiteralPath $Path)
}
function Protect-Secret([string]$Value,[string]$Destination) {
  $bytes=[Text.Encoding]::UTF8.GetBytes($Value)
  try {
    $protected=[Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($Destination,$protected)
  }
  finally { [Array]::Clear($bytes,0,$bytes.Length) }
}
function Lock-File([string]$Path,[string]$ExtraIdentity='') {
  $acl=New-Object Security.AccessControl.FileSecurity
  $acl.SetAccessRuleProtection($true,$false)
  foreach($identity in @('SYSTEM','BUILTIN\Administrators')) { $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','Allow'))) }
  if (-not [string]::IsNullOrWhiteSpace($ExtraIdentity)) { $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($ExtraIdentity,'Read','Allow'))) }
  Set-Acl -LiteralPath $Path -AclObject $acl
}
function Register-MamTask([string]$Name,[string]$Component,[string]$RunnerArgs,[string]$User,[string]$Password) {
  $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$InstallRoot\setup\Start-MamComponent.ps1`" -Component $Component $RunnerArgs"
  $trigger=New-ScheduledTaskTrigger -AtStartup
  $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
  Unregister-ScheduledTask -TaskName $Name -Confirm:$false -ErrorAction SilentlyContinue
  if ($ServiceMode -eq 'System') {
    $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -Principal $principal | Out-Null
  } else {
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -User $User -Password $Password -RunLevel Highest | Out-Null
  }
}

$primary=$PrimaryRoot.Trim(); $backup=$BackupRoot.Trim()
if ([string]::IsNullOrWhiteSpace($primary) -or [string]::IsNullOrWhiteSpace($backup)) { throw 'Primary and Backup storage are required.' }
Assert-Separate $primary $backup 'Primary/Backup'
if ($ApiPort -lt 1 -or $ApiPort -gt 65535 -or $WebPort -lt 1 -or $WebPort -gt 65535 -or $ApiPort -eq $WebPort) { throw 'API and Web ports must be distinct valid TCP ports.' }
if ($RetentionDays -lt 1 -or $AuditRetentionDays -lt 1) { throw 'Retention values must be positive.' }
if ($EnvironmentName -eq 'Production' -and (-not (Test-Path -LiteralPath $TlsPfxPath -PathType Leaf))) { throw 'Production setup requires a PFX certificate.' }
if ($ServiceMode -eq 'Custom' -and ([string]::IsNullOrWhiteSpace($ServiceUser) -or [string]::IsNullOrWhiteSpace($ServicePasswordInputPath))) { throw 'Custom service identity requires user and password.' }

$dataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM'
$configRoot=Join-Path $dataRoot 'config'; $secretRoot=Join-Path $dataRoot 'secrets'; $logRoot=Join-Path $dataRoot 'logs'
New-Item -ItemType Directory -Force -Path $configRoot,$secretRoot,$logRoot | Out-Null
foreach($root in @($primary,$backup)) { if (-not $root.StartsWith('\\')) { New-Item -ItemType Directory -Force -Path $root | Out-Null } }
$errorLog=Join-Path $logRoot 'configure-server-error.log'
Remove-Item -LiteralPath $errorLog -Force -ErrorAction SilentlyContinue

$sql=Read-Secret $SqlSecretInputPath
$servicePassword=''; if ($ServiceMode -eq 'Custom') { $servicePassword=Read-Secret $ServicePasswordInputPath }
$tlsPassword=''; if ($EnvironmentName -eq 'Production') { $tlsPassword=Read-Secret $TlsPasswordInputPath }
try {
  $sqlSecret=Join-Path $secretRoot 'sql.connection.dpapi'; Protect-Secret $sql $sqlSecret; Lock-File $sqlSecret ($(if($ServiceMode -eq 'Custom'){$ServiceUser}else{''}))
  $tlsSecret=''; $tlsInstalled=''
  if ($EnvironmentName -eq 'Production') {
    $tlsSecret=Join-Path $secretRoot 'tls.password.dpapi'; Protect-Secret $tlsPassword $tlsSecret; Lock-File $tlsSecret ($(if($ServiceMode -eq 'Custom'){$ServiceUser}else{''}))
    $tlsInstalled=Join-Path $secretRoot 'server.pfx'; Copy-Item -LiteralPath $TlsPfxPath -Destination $tlsInstalled -Force; Lock-File $tlsInstalled ($(if($ServiceMode -eq 'Custom'){$ServiceUser}else{''}))
  }

  $scheme=if($EnvironmentName -eq 'Production'){'https'}else{'http'}
  $apiPublic="${scheme}://${PublicHost}:$ApiPort"; $webPublic="${scheme}://${PublicHost}:$WebPort"
  $template=Join-Path $InstallRoot 'config\appsettings.Production.template.json'
  $cfg=Get-Content -Raw -LiteralPath $template | ConvertFrom-Json
  $cfg.Environment.Name=$EnvironmentName
  $cfg.Server.PublicBaseUrl=$apiPublic; $cfg.Server.AllowedOrigins=@($webPublic)
  $cfg.Database.ConnectionStringSecretRef='env:MAM_SQL_CONNECTION_STRING'; $cfg.Database.BackupPolicyId=$BackupPolicyId
  $cfg.Storage.Primary.Root=$primary; $cfg.Storage.Primary.CredentialRef='service-identity'; $cfg.Storage.Primary.MinimumFreeGB=10
  $cfg.Storage.Backup.Root=$backup; $cfg.Storage.Backup.CredentialRef='service-identity'; $cfg.Storage.Backup.MinimumFreeGB=10; $cfg.Storage.Backup.IntegrityRecheckSchedule='daily-02:00'
  $cfg.Desktop.IngestCache.MinimumFreeGB=5
  $cfg.Upload.MaxFileSizeGB=4096
  $cfg.Capture.Enabled=$false
  $cfg.Auth.Mode=$AuthMode
  $cfg.Retention.RecycleDays=$RetentionDays
  $cfg.Audit.RetentionDays=$AuditRetentionDays; $cfg.Audit.LogReads=$AuditReadPolicy
  $cfg.Brand.ArabicFontFamily='Segoe UI'; $cfg.Brand.EnglishFontFamily='Segoe UI'; $cfg.Brand.ShowEnvironmentBadge=($EnvironmentName -ne 'Production')
  $configPath=Join-Path $configRoot 'appsettings.Production.json'
  $cfg | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8

  $env:MAM_SQL_CONNECTION_STRING=$sql
  if ($ApplyMigrations -eq 1) {
    & (Join-Path $InstallRoot 'sql\tool\MAM.Deployment.exe') 'ensure-database-env' 'MAM_SQL_CONNECTION_STRING' (Join-Path $InstallRoot 'sql\migrations')
    if ($LASTEXITCODE -ne 0) { throw "Database creation/migration failed with exit code $LASTEXITCODE." }
  }

  $runnerArgs="-InstallRoot `"$InstallRoot`" -ConfigPath `"$configPath`" -SqlSecretPath `"$sqlSecret`" -PublicHost `"$PublicHost`" -ApiPort $ApiPort -WebPort $WebPort -EnvironmentName $EnvironmentName"
  if ($EnvironmentName -eq 'Production') { $runnerArgs += " -TlsPfxPath `"$tlsInstalled`" -TlsSecretPath `"$tlsSecret`"" }
  Register-MamTask 'Diwan MAM API' 'Api' $runnerArgs $ServiceUser $servicePassword
  Register-MamTask 'Diwan MAM Web' 'Web' $runnerArgs $ServiceUser $servicePassword
  Register-MamTask 'Diwan MAM Worker' 'Worker' $runnerArgs $ServiceUser $servicePassword

  if ($OpenFirewall -eq 1) {
    foreach($pair in @(@('Diwan MAM API',$ApiPort),@('Diwan MAM Web',$WebPort))) {
      Remove-NetFirewallRule -DisplayName $pair[0] -ErrorAction SilentlyContinue
      New-NetFirewallRule -DisplayName $pair[0] -Direction Inbound -Action Allow -Protocol TCP -LocalPort ([int]$pair[1]) -Profile Domain,Private | Out-Null
    }
  }
  if ($StartServices -eq 1) { Start-ScheduledTask -TaskName 'Diwan MAM API'; Start-Sleep -Seconds 2; Start-ScheduledTask -TaskName 'Diwan MAM Web'; Start-ScheduledTask -TaskName 'Diwan MAM Worker' }

  [ordered]@{ status='configured'; environment=$EnvironmentName; api=$apiPublic; web=$webPublic; config=$configPath; sqlSecret='DPAPI_LOCAL_MACHINE'; serviceMode=$ServiceMode; primary=$primary; backup=$backup; migrations=($ApplyMigrations -eq 1) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'setup-state.json') -Encoding UTF8
}
catch {
  $diagnostic = @(
    ('type=' + $_.Exception.GetType().FullName),
    ('message=' + $_.Exception.Message),
    ('position=' + $_.InvocationInfo.PositionMessage),
    ('scriptStackTrace=' + $_.ScriptStackTrace)
  ) -join [Environment]::NewLine
  try { Set-Content -LiteralPath $errorLog -Value $diagnostic -Encoding UTF8 -Force } catch { }
  throw
}
finally {
  $env:MAM_SQL_CONNECTION_STRING=$null; $sql=$null; $servicePassword=$null; $tlsPassword=$null
  foreach ($secretInput in @($SqlSecretInputPath,$ServicePasswordInputPath,$TlsPasswordInputPath)) {
    if (-not [string]::IsNullOrWhiteSpace($secretInput)) { Remove-Item -LiteralPath $secretInput -Force -ErrorAction SilentlyContinue }
  }
}
