param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$ContextPath,
  [string]$InstallerPath = '',
  [string]$ReleaseVersion = '',
  [string]$ReleaseCommit = '',
  [string]$MaintenanceHostScriptPath = '',
  [string]$MaintenanceBrandImagePath = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security -ErrorAction Stop
Add-Type -AssemblyName System.Data -ErrorAction Stop

$programDataRoot = Join-Path $env:ProgramData 'Diwan Al Amiri\MAM'
$configPath = Join-Path $programDataRoot 'config\appsettings.Production.json'
$secretRoot = Join-Path $programDataRoot 'secrets'
$sqlSecretPath = Join-Path $secretRoot 'sql.connection.dpapi'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$setupStatePath = Join-Path $programDataRoot 'setup-state.json'
$rollbackBase = Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Rollback'
$persistentRollbackRoot = Join-Path $rollbackBase 'previous'
$tempSafetyRoot = Join-Path 'C:\Temp\MAM\upgrade-safety' $stamp
$currentVersion = ''
try {
  if (Test-Path -LiteralPath $setupStatePath -PathType Leaf) {
    $currentVersion = [string]((Get-Content -Raw -LiteralPath $setupStatePath | ConvertFrom-Json).version)
  }
} catch {}
$usePersistentRollback = -not [string]::IsNullOrWhiteSpace($ReleaseVersion) -and
  -not [string]::Equals($currentVersion,$ReleaseVersion,[StringComparison]::OrdinalIgnoreCase)
$safetyRoot = if ($usePersistentRollback) { $persistentRollbackRoot } else { $tempSafetyRoot }
$sqlPlain = $null
$sqlConnection = $null
$maintenanceRoot = Join-Path $programDataRoot 'maintenance'
$maintenanceStopPath = Join-Path $maintenanceRoot 'stop.signal'
$maintenanceStatePath = Join-Path $maintenanceRoot 'state.json'
$maintenanceStarted = $false
$maintenancePid = 0

function Assert-Admin {
  $id=[Security.Principal.WindowsIdentity]::GetCurrent()
  $p=New-Object Security.Principal.WindowsPrincipal($id)
  if(-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){ throw 'Server Setup must run as Administrator.' }
}
function Canonical([string]$Path) {
  if($Path.StartsWith('\\')){ return $Path.TrimEnd('\') }
  return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}
function Assert-Separate([string]$A,[string]$B,[string]$Label) {
  if([string]::IsNullOrWhiteSpace($A)-or[string]::IsNullOrWhiteSpace($B)){ throw "$Label paths are required." }
  if($A.StartsWith('\\') -or $B.StartsWith('\\')) {
    if($A.TrimEnd('\') -ieq $B.TrimEnd('\')){ throw "$Label paths must be distinct." }
    return
  }
  $a2=(Canonical $A)+'\'; $b2=(Canonical $B)+'\'
  if($a2.StartsWith($b2,[StringComparison]::OrdinalIgnoreCase)-or$b2.StartsWith($a2,[StringComparison]::OrdinalIgnoreCase)){ throw "$Label paths overlap." }
}
function Copy-SafetyTree([string]$Source,[string]$Destination) {
  if(-not(Test-Path -LiteralPath $Source)){ return }
  New-Item -ItemType Directory -Force -Path $Destination|Out-Null
  & robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NFL /NDL /NP
  if($LASTEXITCODE -gt 7){ throw "Safety copy failed: $Source -> $Destination (robocopy=$LASTEXITCODE)." }
}
function Protect-Folder([string]$Path) {
  $acl=New-Object Security.AccessControl.DirectorySecurity
  $acl.SetAccessRuleProtection($true,$false)
  $inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
  foreach($sidValue in @('S-1-5-18','S-1-5-32-544')){
    $sid=New-Object Security.Principal.SecurityIdentifier($sidValue)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)))
  }
  Set-Acl -LiteralPath $Path -AclObject $acl
}
function Unprotect-Secret([string]$Path) {
  if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){ throw "Protected secret not found: $Path" }
  $protected=[IO.File]::ReadAllBytes($Path)
  $plain=[Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
  try { return [Text.Encoding]::UTF8.GetString($plain) }
  finally { if($plain){[Array]::Clear($plain,0,$plain.Length)} }
}
function Test-LocalPort([int]$Port,[int]$Timeout=1000) {
  $client=New-Object Net.Sockets.TcpClient
  try {
    $ar=$client.BeginConnect('127.0.0.1',$Port,$null,$null)
    if(-not $ar.AsyncWaitHandle.WaitOne($Timeout)){return $false}
    $client.EndConnect($ar)
    return $true
  } catch { return $false }
  finally { $client.Close() }
}
function Stop-StaleMaintenance {
  New-Item -ItemType Directory -Force -Path $maintenanceRoot | Out-Null
  if(Test-Path -LiteralPath $maintenanceStatePath -PathType Leaf) {
    try {
      $state=Get-Content -Raw -LiteralPath $maintenanceStatePath | ConvertFrom-Json
      Set-Content -LiteralPath $maintenanceStopPath -Value 'stop' -Encoding ASCII -Force
      if($state.pid) {
        $process=Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
        if($process) {
          try { $process.WaitForExit(5000) } catch {}
          if(-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        }
      }
    } catch {}
  }
  Remove-Item -LiteralPath $maintenanceStopPath,$maintenanceStatePath -Force -ErrorAction SilentlyContinue
}
function Start-MaintenanceHost([string]$Environment,[string]$PublicHost,[int]$WebPort) {
  if([string]::IsNullOrWhiteSpace($MaintenanceHostScriptPath) -or -not(Test-Path -LiteralPath $MaintenanceHostScriptPath -PathType Leaf)){
    throw 'Branded maintenance host script is missing from Server Setup.'
  }
  if([string]::IsNullOrWhiteSpace($MaintenanceBrandImagePath) -or -not(Test-Path -LiteralPath $MaintenanceBrandImagePath -PathType Leaf)){
    throw 'Branded maintenance image is missing from Server Setup.'
  }

  Stop-StaleMaintenance
  Stop-ScheduledTask -TaskName 'Diwan MAM Web' -ErrorAction SilentlyContinue
  Get-Process -Name 'MAM.Web' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

  for($i=0;$i -lt 30 -and (Test-LocalPort -Port $WebPort -Timeout 250);$i++){Start-Sleep -Milliseconds 250}
  if(Test-LocalPort -Port $WebPort -Timeout 250){throw "Web port $WebPort did not become available for maintenance mode."}

  $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  $logPath=Join-Path (Join-Path $programDataRoot 'logs') 'maintenance-host.log'
  $args='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$MaintenanceHostScriptPath+'"'+
    ' -InstallRoot "'+$InstallRoot+'"'+
    ' -EnvironmentName "'+$Environment+'"'+
    ' -PublicHost "'+$PublicHost+'"'+
    ' -WebPort '+$WebPort+
    ' -StopSignalPath "'+$maintenanceStopPath+'"'+
    ' -BrandImagePath "'+$MaintenanceBrandImagePath+'"'+
    ' -LogPath "'+$logPath+'"'
  if($Environment -eq 'Production') {
    $args+=' -TlsPfxPath "'+(Join-Path $secretRoot 'server.pfx')+'"'+
      ' -TlsSecretPath "'+(Join-Path $secretRoot 'tls.password.dpapi')+'"'
  }

  $process=Start-Process -FilePath $powershell -ArgumentList $args -WindowStyle Hidden -PassThru
  $script:maintenancePid=$process.Id

  $ready=$false
  for($i=0;$i -lt 60;$i++) {
    if(Test-LocalPort -Port $WebPort -Timeout 400){$ready=$true;break}
    if($process.HasExited){break}
    Start-Sleep -Milliseconds 250
  }
  if(-not $ready) {
    try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
    throw "Branded maintenance host did not start on port $WebPort."
  }

  $script:maintenanceStarted=$true
  [ordered]@{
    pid=$process.Id
    startedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    stopSignalPath=$maintenanceStopPath
    scriptPath=$MaintenanceHostScriptPath
    brandImagePath=$MaintenanceBrandImagePath
    environment=$Environment
    publicHost=$PublicHost
    webPort=$WebPort
  } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $maintenanceStatePath -Encoding UTF8
}
function Stop-MaintenanceHost {
  if(-not $maintenanceStarted){return}
  try { Set-Content -LiteralPath $maintenanceStopPath -Value 'stop' -Encoding ASCII -Force } catch {}
  if($maintenancePid -gt 0) {
    try {
      $process=Get-Process -Id $maintenancePid -ErrorAction SilentlyContinue
      if($process) {
        try { $process.WaitForExit(5000) } catch {}
        if(-not $process.HasExited){Stop-Process -Id $maintenancePid -Force -ErrorAction SilentlyContinue}
      }
    } catch {}
  }
  Remove-Item -LiteralPath $maintenanceStatePath -Force -ErrorAction SilentlyContinue
  $script:maintenanceStarted=$false
}
function Stop-MamRuntime {
  foreach($task in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')){ Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  foreach($process in @('MAM.Api','MAM.Web','MAM.Worker')){ Get-Process -Name $process -ErrorAction SilentlyContinue|Stop-Process -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
}

try {
  Assert-Admin
  if(-not(Test-Path -LiteralPath $configPath -PathType Leaf)){
    [ordered]@{isUpgrade=$false;preparedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $ContextPath -Encoding UTF8
    exit 0
  }

  $config=Get-Content -Raw -LiteralPath $configPath|ConvertFrom-Json
  $environment=[string]$config.Environment.Name
  if($environment -notin @('Production','UAT')){ throw "Unsupported existing environment '$environment'." }
  $apiUri=[Uri]([string]$config.Server.PublicBaseUrl)
  $origins=@($config.Server.AllowedOrigins)
  if($origins.Count -lt 1){ throw 'Existing Server.AllowedOrigins is empty.' }
  $webUri=[Uri]([string]$origins[0])
  $primary=[string]$config.Storage.Primary.Root
  $backup=[string]$config.Storage.Backup.Root
  Assert-Separate $primary $backup 'Primary/Backup'
  if(-not(Test-Path -LiteralPath $primary)){ throw "Primary storage is not reachable: $primary" }
  if(-not(Test-Path -LiteralPath $backup)){ throw "Backup storage is not reachable: $backup" }
  $installCanonical=Canonical $InstallRoot
  foreach($pair in @(@('Primary',$primary),@('Backup',$backup))){
    if(-not $pair[1].StartsWith('\\')){
      $candidate=Canonical $pair[1]
      if($candidate.StartsWith($installCanonical+'\',[StringComparison]::OrdinalIgnoreCase)){ throw "$($pair[0]) storage cannot be inside the application install directory." }
    }
  }

  Start-MaintenanceHost -Environment $environment -PublicHost $apiUri.Host -WebPort $webUri.Port

  if(Test-Path -LiteralPath $safetyRoot){Remove-Item -LiteralPath $safetyRoot -Recurse -Force}
  New-Item -ItemType Directory -Force -Path $safetyRoot|Out-Null
  Protect-Folder $safetyRoot
  $safetyProgramData=Join-Path $safetyRoot 'ProgramData'
  $safetyInstall=Join-Path $safetyRoot 'Installed'
  $safetyTasks=Join-Path $safetyRoot 'ScheduledTasks'
  Copy-SafetyTree $programDataRoot $safetyProgramData
  if(Test-Path -LiteralPath $InstallRoot){ Copy-SafetyTree $InstallRoot $safetyInstall }
  New-Item -ItemType Directory -Force -Path $safetyTasks|Out-Null

  $serviceMode='System'; $serviceUser=''
  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')){
    $task=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if($task){
      try { Export-ScheduledTask -TaskName $taskName|Set-Content -LiteralPath (Join-Path $safetyTasks (($taskName -replace '[^A-Za-z0-9.-]','_')+'.xml')) -Encoding UTF8 } catch {}
      if($taskName -eq 'Diwan MAM API'){
        $u=[string]$task.Principal.UserId
        if(-not[string]::IsNullOrWhiteSpace($u)-and$u -notin @('SYSTEM','NT AUTHORITY\SYSTEM')){ $serviceMode='Custom'; $serviceUser=$u }
      }
    }
  }

  $sqlPlain=Unprotect-Secret $sqlSecretPath
  $csb=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($sqlPlain)
  $database=[string]$csb.InitialCatalog
  if([string]::IsNullOrWhiteSpace($database)){ throw 'SQL connection string does not specify Initial Catalog/Database.' }
  $sqlConnection=New-Object System.Data.SqlClient.SqlConnection($sqlPlain)
  $sqlConnection.Open()

  $pathCommand=$sqlConnection.CreateCommand()
  $pathCommand.CommandText="SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));"
  $sqlBackupDirectory=[string]$pathCommand.ExecuteScalar()
  if([string]::IsNullOrWhiteSpace($sqlBackupDirectory)){
    $fallback=$sqlConnection.CreateCommand()
    $fallback.CommandText="SELECT TOP (1) physical_name FROM master.sys.master_files WHERE database_id=1 AND file_id=1;"
    $masterFile=[string]$fallback.ExecuteScalar()
    if([string]::IsNullOrWhiteSpace($masterFile)){ throw 'Unable to determine the SQL Server backup directory.' }
    $lastSlash=$masterFile.LastIndexOf('\')
    if($lastSlash -lt 1){ throw 'Unable to derive the SQL Server backup directory.' }
    $sqlBackupDirectory=$masterFile.Substring(0,$lastSlash)
  }
  $sqlBackupDirectory=$sqlBackupDirectory.Trim().TrimEnd([char[]]@('\','/'))
  $safeDb=$database -replace '[^A-Za-z0-9_.-]','_'
  $sqlBackupFile=$sqlBackupDirectory+"\MAM-$safeDb-preupgrade-$stamp.bak"
  $escapedDb=$database.Replace(']',']]')

  $backupCommand=$sqlConnection.CreateCommand()
  $backupCommand.CommandTimeout=0
  $backupCommand.CommandText="BACKUP DATABASE [$escapedDb] TO DISK=@BackupPath WITH COPY_ONLY,CHECKSUM,INIT,STATS=10;"
  $null=$backupCommand.Parameters.Add('@BackupPath',[Data.SqlDbType]::NVarChar,4000)
  $backupCommand.Parameters['@BackupPath'].Value=$sqlBackupFile
  $null=$backupCommand.ExecuteNonQuery()

  $verifyCommand=$sqlConnection.CreateCommand()
  $verifyCommand.CommandTimeout=0
  $verifyCommand.CommandText='RESTORE VERIFYONLY FROM DISK=@BackupPath WITH CHECKSUM;'
  $null=$verifyCommand.Parameters.Add('@BackupPath',[Data.SqlDbType]::NVarChar,4000)
  $verifyCommand.Parameters['@BackupPath'].Value=$sqlBackupFile
  $null=$verifyCommand.ExecuteNonQuery()
  $sqlConnection.Close(); $sqlConnection.Dispose(); $sqlConnection=$null

  $installerHash=$null
  if(-not[string]::IsNullOrWhiteSpace($InstallerPath)-and(Test-Path -LiteralPath $InstallerPath -PathType Leaf)){
    $installerHash=(Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPath=Join-Path (Split-Path -Parent $InstallerPath) 'setup-manifest.json'
    if(Test-Path -LiteralPath $manifestPath -PathType Leaf){
      $manifest=Get-Content -Raw -LiteralPath $manifestPath|ConvertFrom-Json
      $entry=@($manifest.artifacts|Where-Object{$_.file -eq [IO.Path]::GetFileName($InstallerPath)})
      if($entry.Count -ne 1){ throw 'setup-manifest.json does not contain this Server Setup exactly once.' }
      if($installerHash -ne ([string]$entry[0].sha256).ToLowerInvariant()){ throw 'Server Setup SHA256 does not match setup-manifest.json.' }
    }
  }

  $context=[ordered]@{
    isUpgrade=$true
    preparedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    safetyRoot=$safetyRoot
    oldConfigPath=Join-Path $safetyProgramData 'config\appsettings.Production.json'
    oldSqlSecretPath=Join-Path $safetyProgramData 'secrets\sql.connection.dpapi'
    oldInternalAuthSecretPath=Join-Path $safetyProgramData 'secrets\internal-auth.dpapi'
    oldTlsPfxPath=Join-Path $safetyProgramData 'secrets\server.pfx'
    oldTlsSecretPath=Join-Path $safetyProgramData 'secrets\tls.password.dpapi'
    environment=$environment
    publicHost=$apiUri.Host
    apiPort=$apiUri.Port
    webPort=$webUri.Port
    primaryRoot=$primary
    backupRoot=$backup
    backupPolicyId=[string]$config.Database.BackupPolicyId
    authMode=[string]$config.Auth.Mode
    retentionDays=[int]$config.Retention.RecycleDays
    auditRetentionDays=[int]$config.Audit.RetentionDays
    auditReadPolicy=[string]$config.Audit.LogReads
    serviceMode=$serviceMode
    serviceUser=$serviceUser
    database=$database
    sqlBackup=$sqlBackupFile
    sqlBackupVerified=$true
    installerPath=$InstallerPath
    installerSha256=$installerHash
    releaseVersion=$ReleaseVersion
    releaseCommit=$ReleaseCommit
    previousVersion=$currentVersion
    rollbackRoot=$persistentRollbackRoot
    persistentRollback=$usePersistentRollback
    maintenanceActive=$maintenanceStarted
    maintenancePid=$maintenancePid
    maintenanceStopPath=$maintenanceStopPath
    maintenanceStatePath=$maintenanceStatePath
    maintenanceHostScriptPath=$MaintenanceHostScriptPath
    maintenanceBrandImagePath=$MaintenanceBrandImagePath
  }
  $context|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $ContextPath -Encoding UTF8
  $context|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $safetyRoot 'pre-upgrade-evidence.json') -Encoding UTF8

  Stop-MamRuntime
  exit 0
}
catch {
  try { if($sqlConnection){$sqlConnection.Close();$sqlConnection.Dispose()} } catch {}
  try {
    Stop-MaintenanceHost
    Start-ScheduledTask -TaskName 'Diwan MAM Web' -ErrorAction SilentlyContinue
  } catch {}
  $message=@"
MAM SERVER PRE-UPGRADE FAILED
Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Message: $($_.Exception.Message)
SafetySet: $safetyRoot
FullError:
$($_ | Out-String)
"@
  try {
    New-Item -ItemType Directory -Force -Path 'C:\Temp\MAM'|Out-Null
    $message|Set-Content -LiteralPath 'C:\Temp\MAM\MAM-Server-Setup-PreUpgrade-Error.txt' -Encoding UTF8
  } catch {}
  Write-Error $_
  exit 1
}
finally {
  $sqlPlain=$null
  $sqlConnection=$null
}
