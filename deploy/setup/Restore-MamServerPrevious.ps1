param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$RollbackRoot,
  [Parameter(Mandatory=$true)][string]$MaintenanceHostScriptPath,
  [Parameter(Mandatory=$true)][string]$MaintenanceBrandImagePath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$programDataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM'
$logRoot=Join-Path $programDataRoot 'logs'
$sourceInstall=Join-Path $RollbackRoot 'Installed'
$sourceProgramData=Join-Path $RollbackRoot 'ProgramData'
$sourceConfig=Join-Path $sourceProgramData 'config'
$sourceSecrets=Join-Path $sourceProgramData 'secrets'
$sourceState=Join-Path $sourceProgramData 'setup-state.json'
$evidencePath=Join-Path $RollbackRoot 'pre-upgrade-evidence.json'
$rollbackLog=Join-Path $logRoot 'restore-previous-version.log'
$maintenanceRoot=Join-Path $programDataRoot 'maintenance'
$maintenanceStopPath=Join-Path $maintenanceRoot 'stop.signal'
$maintenanceStatePath=Join-Path $maintenanceRoot 'state.json'
$maintenanceProcess=$null
$maintenanceStarted=$false

function Assert-Admin {
  $id=[Security.Principal.WindowsIdentity]::GetCurrent()
  $principal=New-Object Security.Principal.WindowsPrincipal($id)
  if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    throw 'Previous-version restore must run as Administrator.'
  }
}

function Stop-StaleMaintenance {
  New-Item -ItemType Directory -Force -Path $maintenanceRoot | Out-Null
  if(Test-Path -LiteralPath $maintenanceStatePath -PathType Leaf){
    try{
      $state=Get-Content -Raw -LiteralPath $maintenanceStatePath|ConvertFrom-Json
      Set-Content -LiteralPath $maintenanceStopPath -Value 'stop' -Encoding ASCII -Force
      if($state.pid){
        $p=Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
        if($p){
          try{$p.WaitForExit(5000)}catch{}
          if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
        }
      }
    }catch{}
  }
  Remove-Item -LiteralPath $maintenanceStopPath,$maintenanceStatePath -Force -ErrorAction SilentlyContinue
}
function Start-MaintenanceHost($Config){
  if(-not(Test-Path -LiteralPath $MaintenanceHostScriptPath -PathType Leaf)){throw 'Maintenance host script is missing.'}
  if(-not(Test-Path -LiteralPath $MaintenanceBrandImagePath -PathType Leaf)){throw 'Maintenance brand image is missing.'}
  $environment=[string]$Config.Environment.Name
  $apiUri=[Uri]([string]$Config.Server.PublicBaseUrl)
  $origins=@($Config.Server.AllowedOrigins)
  if($origins.Count -lt 1){throw 'Current Server.AllowedOrigins is empty.'}
  $webUri=[Uri]([string]$origins[0])

  Stop-StaleMaintenance
  Stop-ScheduledTask -TaskName 'Diwan MAM Web' -ErrorAction SilentlyContinue
  Get-Process -Name 'MAM.Web' -ErrorAction SilentlyContinue|Stop-Process -Force -ErrorAction SilentlyContinue
  for($i=0;$i -lt 30 -and (Test-Tcp -Port $webUri.Port -Timeout 250);$i++){Start-Sleep -Milliseconds 250}
  if(Test-Tcp -Port $webUri.Port -Timeout 250){throw "Web port $($webUri.Port) did not become available for maintenance mode."}

  $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  $args='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$MaintenanceHostScriptPath+'"'+
    ' -InstallRoot "'+$InstallRoot+'"'+
    ' -EnvironmentName "'+$environment+'"'+
    ' -PublicHost "'+$webUri.Host+'"'+
    ' -WebPort '+$webUri.Port+
    ' -StopSignalPath "'+$maintenanceStopPath+'"'+
    ' -BrandImagePath "'+$MaintenanceBrandImagePath+'"'+
    ' -LogPath "'+(Join-Path $logRoot 'maintenance-host.log')+'"'
  if($environment -eq 'Production'){
    $args+=' -TlsPfxPath "'+(Join-Path $programDataRoot 'secrets\server.pfx')+'"'+
      ' -TlsSecretPath "'+(Join-Path $programDataRoot 'secrets\tls.password.dpapi')+'"'
  }
  $script:maintenanceProcess=Start-Process -FilePath $powershell -ArgumentList $args -WindowStyle Hidden -PassThru
  $ready=$false
  for($i=0;$i -lt 60;$i++){
    if(Test-Tcp -Port $webUri.Port -Timeout 400){$ready=$true;break}
    if($maintenanceProcess.HasExited){break}
    Start-Sleep -Milliseconds 250
  }
  if(-not$ready){throw "Maintenance host did not start on port $($webUri.Port)."}
  $script:maintenanceStarted=$true
  [ordered]@{pid=$maintenanceProcess.Id;startedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');stopSignalPath=$maintenanceStopPath;restorePrevious=$true}|ConvertTo-Json|Set-Content -LiteralPath $maintenanceStatePath -Encoding UTF8
}
function Stop-MaintenanceHost {
  if(-not$maintenanceStarted){return}
  try{Set-Content -LiteralPath $maintenanceStopPath -Value 'stop' -Encoding ASCII -Force}catch{}
  if($maintenanceProcess){
    try{$maintenanceProcess.WaitForExit(7000)}catch{}
    if(-not$maintenanceProcess.HasExited){Stop-Process -Id $maintenanceProcess.Id -Force -ErrorAction SilentlyContinue}
  }
  Remove-Item -LiteralPath $maintenanceStatePath -Force -ErrorAction SilentlyContinue
  $script:maintenanceStarted=$false
}
function Stop-MamRuntime {
  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')){
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  }
  Start-Sleep -Seconds 2
  foreach($processName in @('MAM.Api','MAM.Web','MAM.Worker')){
    Get-Process -Name $processName -ErrorAction SilentlyContinue |
      Stop-Process -Force -ErrorAction SilentlyContinue
  }
  Start-Sleep -Seconds 2
}

function Copy-Mirror([string]$Source,[string]$Destination) {
  if(-not(Test-Path -LiteralPath $Source -PathType Container)){
    throw "Rollback source folder is missing: $Source"
  }
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  & robocopy $Source $Destination /MIR /COPY:DAT /DCOPY:DAT /R:2 /W:1 /XJ /NFL /NDL /NP /XF 'unins*.exe' 'unins*.dat'
  if($LASTEXITCODE -gt 7){
    throw "Rollback copy failed: $Source -> $Destination (robocopy=$LASTEXITCODE)."
  }
}

function Copy-Tree([string]$Source,[string]$Destination) {
  if(-not(Test-Path -LiteralPath $Source -PathType Container)){
    throw "Rollback source folder is missing: $Source"
  }
  if(Test-Path -LiteralPath $Destination){Remove-Item -LiteralPath $Destination -Recurse -Force}
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  & robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /XJ /NFL /NDL /NP
  if($LASTEXITCODE -gt 7){
    throw "Rollback data copy failed: $Source -> $Destination (robocopy=$LASTEXITCODE)."
  }
}

function Restore-MissingSystemTask([string]$TaskName) {
  if(Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue){return}
  $xmlName=($TaskName -replace '[^A-Za-z0-9.-]','_')+'.xml'
  $xmlPath=Join-Path (Join-Path $RollbackRoot 'ScheduledTasks') $xmlName
  if(-not(Test-Path -LiteralPath $xmlPath -PathType Leaf)){
    throw "Scheduled task '$TaskName' is missing and no rollback task definition exists."
  }
  $xml=Get-Content -Raw -LiteralPath $xmlPath
  if($xml -notmatch '<UserId>(SYSTEM|S-1-5-18|NT AUTHORITY\\SYSTEM)</UserId>'){
    throw "Scheduled task '$TaskName' uses a custom identity and cannot be recreated without its password. The existing task must remain present for rollback."
  }
  Register-ScheduledTask -TaskName $TaskName -Xml $xml -Force | Out-Null
}

function Test-Tcp([int]$Port,[int]$Timeout=7000) {
  $client=New-Object Net.Sockets.TcpClient
  try{
    $ar=$client.BeginConnect('127.0.0.1',$Port,$null,$null)
    if(-not $ar.AsyncWaitHandle.WaitOne($Timeout)){return $false}
    $client.EndConnect($ar)
    return $true
  }
  catch{return $false}
  finally{$client.Close()}
}

try{
  Assert-Admin
  New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

  foreach($required in @(
    (Join-Path $sourceInstall 'api\MAM.Api.exe'),
    (Join-Path $sourceInstall 'web\MAM.Web.exe'),
    (Join-Path $sourceInstall 'worker\MAM.Worker.exe'),
    (Join-Path $sourceInstall 'setup\Start-MamComponent.ps1'),
    (Join-Path $sourceConfig 'appsettings.Production.json'),
    (Join-Path $sourceSecrets 'sql.connection.dpapi'),
    $evidencePath
  )){
    if(-not(Test-Path -LiteralPath $required)){
      throw "Previous-version restore point is incomplete: $required"
    }
  }

  $evidence=Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
  $currentConfigPath=Join-Path $programDataRoot 'config\appsettings.Production.json'
  if(-not(Test-Path -LiteralPath $currentConfigPath -PathType Leaf)){throw 'Current server configuration is missing.'}
  $currentConfig=Get-Content -Raw -LiteralPath $currentConfigPath|ConvertFrom-Json
  Start-MaintenanceHost $currentConfig
  Stop-MamRuntime

  # Restore application binaries/scripts exactly to the protected pre-upgrade tree.
  # This intentionally does not restore SQL or Primary/Backup media data.
  Copy-Mirror $sourceInstall $InstallRoot

  # Restore only configuration/secrets/state. Runtime logs stay current so the
  # rollback operation remains auditable.
  Copy-Tree $sourceConfig (Join-Path $programDataRoot 'config')
  Copy-Tree $sourceSecrets (Join-Path $programDataRoot 'secrets')
  if(Test-Path -LiteralPath $sourceState -PathType Leaf){
    Copy-Item -LiteralPath $sourceState -Destination (Join-Path $programDataRoot 'setup-state.json') -Force
  }

  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')){
    Restore-MissingSystemTask $taskName
  }

  $restoredConfig=Get-Content -Raw -LiteralPath (Join-Path $programDataRoot 'config\appsettings.Production.json') | ConvertFrom-Json
  $apiUri=[Uri]([string]$restoredConfig.Server.PublicBaseUrl)
  $webOrigins=@($restoredConfig.Server.AllowedOrigins)
  if($webOrigins.Count -lt 1){throw 'Restored Server.AllowedOrigins is empty.'}
  $webUri=[Uri]([string]$webOrigins[0])

  Start-ScheduledTask -TaskName 'Diwan MAM API'
  Start-Sleep -Seconds 5
  Stop-MaintenanceHost
  Start-ScheduledTask -TaskName 'Diwan MAM Web'
  Start-ScheduledTask -TaskName 'Diwan MAM Worker'
  Start-Sleep -Seconds 8

  if(-not(Test-Tcp -Port $apiUri.Port)){throw "Restored API is not listening on localhost:$($apiUri.Port)."}
  if(-not(Test-Tcp -Port $webUri.Port)){throw "Restored Web is not listening on localhost:$($webUri.Port)."}

  $restoredVersion=''
  try{
    $statePath=Join-Path $programDataRoot 'setup-state.json'
    if(Test-Path -LiteralPath $statePath -PathType Leaf){
      $restoredVersion=[string]((Get-Content -Raw -LiteralPath $statePath|ConvertFrom-Json).version)
    }
  }catch{}
  if([string]::IsNullOrWhiteSpace($restoredVersion)){
    try{$restoredVersion=[string]$evidence.previousVersion}catch{}
  }

  if(-not[string]::IsNullOrWhiteSpace($restoredVersion)){
    foreach($uninstallKey in @(
      'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A97CA8AB-17A1-4A88-9C39-B06A2A4D49F7}_is1',
      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{A97CA8AB-17A1-4A88-9C39-B06A2A4D49F7}_is1'
    )){
      if(Test-Path -LiteralPath $uninstallKey){
        try{
          Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $restoredVersion -Force
          Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayName -Value ("Diwan Al Amiri MAM Server "+$restoredVersion) -Force
        }catch{}
      }
    }
  }

  [ordered]@{
    result='SUCCESS'
    restoredAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    restoredVersion=$restoredVersion
    rollbackRoot=$RollbackRoot
    applicationFilesRestored=$true
    configurationRestored=$true
    secretsRestored=$true
    databaseRestored=$false
    mediaStorageRestored=$false
    databaseNote='Database is intentionally kept at its current forward-compatible state to avoid losing post-upgrade data.'
    apiPort=$apiUri.Port
    webPort=$webUri.Port
  } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $programDataRoot 'last-rollback.json') -Encoding UTF8

  @(
    'MAM PREVIOUS VERSION RESTORE: SUCCESS',
    ('Date: '+(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
    ('RestoredVersion: '+$restoredVersion),
    ('RollbackRoot: '+$RollbackRoot),
    'DatabaseRestored: False',
    'MediaStorageRestored: False'
  ) | Set-Content -LiteralPath $rollbackLog -Encoding UTF8

  exit 0
}
catch{
  try{
    if(-not $maintenanceStarted){
      $fallbackConfigPath=Join-Path $programDataRoot 'config\appsettings.Production.json'
      if(Test-Path -LiteralPath $fallbackConfigPath -PathType Leaf){
        $fallbackConfig=Get-Content -Raw -LiteralPath $fallbackConfigPath|ConvertFrom-Json
        Start-MaintenanceHost $fallbackConfig
      }
    }
  }catch{}
  try{
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    @(
      'MAM PREVIOUS VERSION RESTORE: FAILED',
      ('Date: '+(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
      ('Message: '+$_.Exception.Message),
      ('RollbackRoot: '+$RollbackRoot),
      '',
      ($_ | Out-String)
    ) | Set-Content -LiteralPath $rollbackLog -Encoding UTF8
  }catch{}
  Write-Error $_
  exit 1
}
