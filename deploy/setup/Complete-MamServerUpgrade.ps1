param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$ContextPath,
  [string]$ReleaseVersion = ''
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security -ErrorAction Stop
Add-Type -AssemblyName System.Data -ErrorAction Stop

$programDataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM'
$configRoot=Join-Path $programDataRoot 'config'
$secretRoot=Join-Path $programDataRoot 'secrets'
$logRoot=Join-Path $programDataRoot 'logs'
$configPath=Join-Path $configRoot 'appsettings.Production.json'
$setupStatePath=Join-Path $programDataRoot 'setup-state.json'
$sqlSecretPath=Join-Path $secretRoot 'sql.connection.dpapi'
$internalAuthSecretPath=Join-Path $secretRoot 'internal-auth.dpapi'
$tlsPfxPath=Join-Path $secretRoot 'server.pfx'
$tlsSecretPath=Join-Path $secretRoot 'tls.password.dpapi'
$sqlPlain=$null

function Merge-OldIntoNew($NewObject,$OldObject){
  foreach($property in $OldObject.PSObject.Properties){
    $name=$property.Name
    $oldValue=$property.Value
    $newProperty=$NewObject.PSObject.Properties[$name]
    if($null -eq $newProperty){
      $NewObject|Add-Member -MemberType NoteProperty -Name $name -Value $oldValue
      continue
    }
    $newValue=$newProperty.Value
    if($oldValue -is [PSCustomObject] -and $newValue -is [PSCustomObject]){ Merge-OldIntoNew $newValue $oldValue }
    else { $NewObject.$name=$oldValue }
  }
}
function Unprotect-Secret([string]$Path){
  if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){ throw "Protected secret not found: $Path" }
  $protected=[IO.File]::ReadAllBytes($Path)
  $plain=[Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
  try{return [Text.Encoding]::UTF8.GetString($plain)}
  finally{if($plain){[Array]::Clear($plain,0,$plain.Length)}}
}
function Lock-File([string]$Path,[string]$ExtraIdentity=''){
  $acl=New-Object Security.AccessControl.FileSecurity
  $acl.SetAccessRuleProtection($true,$false)
  foreach($identity in @('SYSTEM','BUILTIN\Administrators')){
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','Allow')))
  }
  if(-not[string]::IsNullOrWhiteSpace($ExtraIdentity)){
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($ExtraIdentity,'Read','Allow')))
  }
  Set-Acl -LiteralPath $Path -AclObject $acl
}
function Register-SystemTask([string]$Name,[string]$Component,[string]$RunnerArgs){
  $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$InstallRoot\setup\Start-MamComponent.ps1`" -Component $Component $RunnerArgs"
  $trigger=New-ScheduledTaskTrigger -AtStartup
  $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
  Unregister-ScheduledTask -TaskName $Name -Confirm:$false -ErrorAction SilentlyContinue
  $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
  Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -Principal $principal|Out-Null
}
function Test-Tcp([string]$HostName,[int]$Port,[int]$Timeout=5000){
  $client=New-Object Net.Sockets.TcpClient
  try{
    $ar=$client.BeginConnect($HostName,$Port,$null,$null)
    if(-not$ar.AsyncWaitHandle.WaitOne($Timeout)){return $false}
    $client.EndConnect($ar)
    return $true
  }
  catch{return $false}
  finally{$client.Close()}
}
function Invoke-LocalHttpGet(
  [string]$HostName,
  [int]$Port,
  [string]$Path,
  [bool]$UseTls,
  [hashtable]$Headers = @{}
){
  $client=New-Object System.Net.Sockets.TcpClient
  $stream=$null
  $ssl=$null
  $reader=$null
  try{
    $client.Connect('127.0.0.1',$Port)
    $stream=$client.GetStream()

    if($UseTls){
      $validationCallback={
        param($sender,$certificate,$chain,$sslPolicyErrors)
        return $true
      }
      $ssl=New-Object System.Net.Security.SslStream($stream,$false,$validationCallback)
      $ssl.AuthenticateAsClient(
        $HostName,
        $null,
        [System.Security.Authentication.SslProtocols]::Tls12,
        $false
      )
      $stream=$ssl
    }

    if(($UseTls -and $Port -eq 443) -or (-not $UseTls -and $Port -eq 80)){
      $hostHeader=$HostName
    }
    else{
      $hostHeader=$HostName + ':' + $Port
    }

    $requestLines=New-Object System.Collections.Generic.List[string]
    $requestLines.Add('GET ' + $Path + ' HTTP/1.1')
    $requestLines.Add('Host: ' + $hostHeader)
    $requestLines.Add('Accept: application/json')
    foreach($keyName in $Headers.Keys){
      $requestLines.Add($keyName + ': ' + [string]$Headers[$keyName])
    }
    $requestLines.Add('Connection: close')

    $crlf=[string][char]13 + [string][char]10
    $request=($requestLines -join $crlf) + $crlf + $crlf
    $bytes=[Text.Encoding]::ASCII.GetBytes($request)
    $stream.Write($bytes,0,$bytes.Length)
    $stream.Flush()

    $reader=New-Object IO.StreamReader($stream,[Text.Encoding]::ASCII,$false,4096,$true)
    $statusLine=$reader.ReadLine()
    if([string]::IsNullOrWhiteSpace($statusLine) -or $statusLine -notmatch '^HTTP/\d(?:\.\d)?\s+(\d{3})'){
      throw "Invalid HTTP response from local MAM endpoint '$Path': $statusLine"
    }

    return [int]$matches[1]
  }
  finally{
    if($reader){$reader.Dispose()}
    if($ssl){$ssl.Dispose()}
    elseif($stream){$stream.Dispose()}
    $client.Close()
  }
}

function Invoke-Health([string]$HostName,[int]$Port,[string]$Path,[bool]$UseTls){
  return Invoke-LocalHttpGet -HostName $HostName -Port $Port -Path $Path -UseTls $UseTls
}

function Test-SignedMamSession([string]$HostName,[int]$Port,[bool]$UseTls,[string]$ConnectionString,[string]$InternalSecretPath){
  if([string]$context.authMode -ne 'ActiveDirectory'){
    return [ordered]@{tested=$false;status='NOT_APPLICABLE';user=$null}
  }

  $connection=New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
  try{
    $connection.Open()
    $countCommand=$connection.CreateCommand()
    $countCommand.CommandText=@"
SELECT
  COUNT(DISTINCT CASE WHEN u.IsEnabled=1 THEN u.UserId END) EnabledUsers,
  COUNT(DISTINCT CASE WHEN u.IsEnabled=1 AND ur.RoleId IS NOT NULL THEN u.UserId END) EnabledUsersWithRoles
FROM dbo.MamUser u
LEFT JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId;
"@
    $reader=$countCommand.ExecuteReader()
    try{
      if(-not$reader.Read()){throw 'Unable to verify the authoritative MAM user store.'}
      $enabled=$reader.GetInt32(0)
      $withRoles=$reader.GetInt32(1)
    }
    finally{$reader.Close()}
    if($enabled -lt 1){throw 'No enabled MAM users exist after the upgrade.'}
    if($withRoles -lt 1){throw 'No enabled MAM user has a role after the upgrade.'}

    $userCommand=$connection.CreateCommand()
    $userCommand.CommandText=@"
SELECT TOP (1) u.UserName
FROM dbo.MamUser u
WHERE u.IsEnabled=1
  AND EXISTS (SELECT 1 FROM dbo.MamUserRole ur WHERE ur.UserId=u.UserId)
ORDER BY u.UserName;
"@
    $testUser=[string]$userCommand.ExecuteScalar()
    if([string]::IsNullOrWhiteSpace($testUser)){throw 'Unable to select an enabled MAM identity for signed-session verification.'}
  }
  finally{
    $connection.Close()
    $connection.Dispose()
  }

  $internalKeyB64=Unprotect-Secret $InternalSecretPath
  $key=$null
  try{
    $key=[Convert]::FromBase64String($internalKeyB64)
    if($key.Length -lt 32){throw 'The restored internal authentication key is invalid.'}
    $timestamp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $target='/api/v1/session'
    $lf=[string][char]10
    $canonical=$testUser+$lf+$timestamp+$lf+'GET'+$lf+$target

    $hmac=New-Object System.Security.Cryptography.HMACSHA256
    try{
      $hmac.Key=$key
      $signature=[Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical)))
    }
    finally{$hmac.Dispose()}

    $headers=@{
      'X-MAM-Auth-User'=$testUser
      'X-MAM-Auth-Timestamp'=$timestamp
      'X-MAM-Auth-Signature'=$signature
    }

    $status=Invoke-LocalHttpGet -HostName $HostName -Port $Port -Path $target -UseTls $UseTls -Headers $headers
    if($status -lt 200 -or $status -ge 300){
      throw "Signed MAM session returned HTTP $status."
    }

    return [ordered]@{
      tested=$true
      status="HTTP_$status"
      user=$testUser
      enabledUsers=$enabled
      enabledUsersWithRoles=$withRoles
    }
  }
  catch{
    throw "Post-upgrade Web/API authentication verification failed: $($_.Exception.Message)"
  }
  finally{
    if($key){[Array]::Clear($key,0,$key.Length)}
    $internalKeyB64=$null
  }
}
function Safe-Token([string]$Value){ return ($Value -replace '[^A-Za-z0-9.-]','-') }

try{
  if(-not(Test-Path -LiteralPath $ContextPath -PathType Leaf)){throw "Upgrade context not found: $ContextPath"}
  $context=Get-Content -Raw -LiteralPath $ContextPath|ConvertFrom-Json
  if(-not[bool]$context.isUpgrade){throw 'The supplied context is not an upgrade context.'}

  $safetyRoot=[string]$context.safetyRoot
  if(-not(Test-Path -LiteralPath $safetyRoot -PathType Container)){throw "Safety set not found: $safetyRoot"}
  New-Item -ItemType Directory -Force -Path $configRoot,$secretRoot,$logRoot|Out-Null

  if(-not(Test-Path -LiteralPath ([string]$context.oldConfigPath) -PathType Leaf)){throw 'Pre-upgrade config is missing from the safety set.'}
  $oldConfig=Get-Content -Raw -LiteralPath ([string]$context.oldConfigPath)|ConvertFrom-Json
  $templatePath=Join-Path $InstallRoot 'config\appsettings.Production.template.json'
  if(-not(Test-Path -LiteralPath $templatePath -PathType Leaf)){throw "New configuration template not found: $templatePath"}
  $newConfig=Get-Content -Raw -LiteralPath $templatePath|ConvertFrom-Json
  Merge-OldIntoNew $newConfig $oldConfig

  $scheme=if([string]$context.environment -eq 'Production'){'https'}else{'http'}
  $newConfig.Environment.Name=[string]$context.environment
  $newConfig.Server.PublicBaseUrl="$($scheme)://$([string]$context.publicHost):$([int]$context.apiPort)"
  $newConfig.Server.AllowedOrigins=@("$($scheme)://$([string]$context.publicHost):$([int]$context.webPort)")
  $newConfig.Storage.Primary.Root=[string]$context.primaryRoot
  $newConfig.Storage.Backup.Root=[string]$context.backupRoot
  $newConfig.Database.BackupPolicyId=[string]$context.backupPolicyId
  $newConfig.Auth.Mode=[string]$context.authMode
  $newConfig.Retention.RecycleDays=[int]$context.retentionDays
  $newConfig.Audit.RetentionDays=[int]$context.auditRetentionDays
  $newConfig.Audit.LogReads=[string]$context.auditReadPolicy
  $newConfig|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $configPath -Encoding UTF8

  foreach($pair in @(
    @([string]$context.oldSqlSecretPath,$sqlSecretPath),
    @([string]$context.oldInternalAuthSecretPath,$internalAuthSecretPath)
  )){
    if(-not(Test-Path -LiteralPath $pair[0] -PathType Leaf)){throw "Required pre-upgrade secret missing: $($pair[0])"}
    Copy-Item -LiteralPath $pair[0] -Destination $pair[1] -Force
  }

  $extraIdentity=if([string]$context.serviceMode -eq 'Custom'){[string]$context.serviceUser}else{''}
  Lock-File $sqlSecretPath $extraIdentity
  Lock-File $internalAuthSecretPath $extraIdentity

  if([string]$context.environment -eq 'Production'){
    foreach($pair in @(
      @([string]$context.oldTlsPfxPath,$tlsPfxPath),
      @([string]$context.oldTlsSecretPath,$tlsSecretPath)
    )){
      if(-not(Test-Path -LiteralPath $pair[0] -PathType Leaf)){throw "Required pre-upgrade TLS material missing: $($pair[0])"}
      Copy-Item -LiteralPath $pair[0] -Destination $pair[1] -Force
      Lock-File $pair[1] $extraIdentity
    }
  }

  $sqlPlain=Unprotect-Secret $sqlSecretPath
  $env:MAM_SQL_CONNECTION_STRING=$sqlPlain
  & (Join-Path $InstallRoot 'sql\tool\MAM.Deployment.exe') 'ensure-database-env' 'MAM_SQL_CONNECTION_STRING' (Join-Path $InstallRoot 'sql\migrations')
  if($LASTEXITCODE -ne 0){throw "Database migration failed with exit code $LASTEXITCODE."}

  $runnerArgs="-InstallRoot `"$InstallRoot`" -ConfigPath `"$configPath`" -SqlSecretPath `"$sqlSecretPath`" -InternalAuthSecretPath `"$internalAuthSecretPath`" -PublicHost `"$([string]$context.publicHost)`" -ApiPort $([int]$context.apiPort) -WebPort $([int]$context.webPort) -EnvironmentName $([string]$context.environment)"
  if([string]$context.environment -eq 'Production'){
    $runnerArgs+=" -TlsPfxPath `"$tlsPfxPath`" -TlsSecretPath `"$tlsSecretPath`""
  }

  if([string]$context.serviceMode -eq 'System'){
    Register-SystemTask 'Diwan MAM API' 'Api' $runnerArgs
    Register-SystemTask 'Diwan MAM Web' 'Web' $runnerArgs
    Register-SystemTask 'Diwan MAM Worker' 'Worker' $runnerArgs
  }
  else{
    foreach($name in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')){
      $task=Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
      if(-not$task){throw "Custom-identity upgrade cannot recreate missing scheduled task '$name' without the service-account password."}
      $actionText=($task.Actions|ForEach-Object{"$($_.Execute) $($_.Arguments)"}) -join ' '
      if($actionText -notmatch 'Start-MamComponent\.ps1' -or $actionText -notmatch 'InternalAuthSecretPath'){
        throw "Custom-identity task '$name' uses an obsolete runtime contract. Reconfiguration requires the service-account password."
      }
    }
  }

  foreach($pair in @(
    @('Diwan MAM API',[int]$context.apiPort),
    @('Diwan MAM Web',[int]$context.webPort)
  )){
    Remove-NetFirewallRule -DisplayName $pair[0] -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $pair[0] -Direction Inbound -Action Allow -Protocol TCP -LocalPort $pair[1] -Profile Domain,Private|Out-Null
  }

  $sourceDesktop=Join-Path $InstallRoot 'downloads\DiwanMAM-Desktop-Setup.exe'
  if(Test-Path -LiteralPath $sourceDesktop -PathType Leaf){
    $downloadDir=Join-Path $InstallRoot 'web\wwwroot\downloads'
    New-Item -ItemType Directory -Force -Path $downloadDir|Out-Null
    $hostToken=Safe-Token ([string]$context.publicHost)
    $fileName="DiwanMAM-Desktop-Setup--$($scheme)--$hostToken--$([int]$context.apiPort)--$ReleaseVersion.exe"
    Copy-Item -LiteralPath $sourceDesktop -Destination (Join-Path $downloadDir $fileName) -Force
    [ordered]@{
      url="/downloads/$fileName"
      apiBaseUrl="$($scheme)://$([string]$context.publicHost):$([int]$context.apiPort)"
      environment=[string]$context.environment
      version=$ReleaseVersion
    }|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $InstallRoot 'web\wwwroot\desktop-download.json') -Encoding UTF8
  }

  Start-ScheduledTask -TaskName 'Diwan MAM API'
  Start-Sleep -Seconds 5
  Start-ScheduledTask -TaskName 'Diwan MAM Web'
  Start-ScheduledTask -TaskName 'Diwan MAM Worker'
  Start-Sleep -Seconds 8

  if(-not(Test-Tcp '127.0.0.1' ([int]$context.apiPort))){throw "API is not listening on localhost:$([int]$context.apiPort)."}
  if(-not(Test-Tcp '127.0.0.1' ([int]$context.webPort))){throw "Web is not listening on localhost:$([int]$context.webPort)."}

  $useTls=[string]$context.environment -eq 'Production'
  $apiStatus=Invoke-Health -HostName ([string]$context.publicHost) -Port ([int]$context.apiPort) -Path '/health/live' -UseTls $useTls
  $webStatus=Invoke-Health -HostName ([string]$context.publicHost) -Port ([int]$context.webPort) -Path '/auth/status' -UseTls $useTls
  if($apiStatus -lt 200 -or $apiStatus -ge 300){throw "API health returned HTTP $apiStatus."}
  if($webStatus -lt 200 -or $webStatus -ge 300){throw "Web auth status returned HTTP $webStatus."}

  $authVerification=Test-SignedMamSession -HostName ([string]$context.publicHost) -Port ([int]$context.apiPort) -UseTls $useTls -ConnectionString $sqlPlain -InternalSecretPath $internalAuthSecretPath

  [ordered]@{
    status='configured'
    upgrade=$true
    version=$ReleaseVersion
    environment=[string]$context.environment
    api=$newConfig.Server.PublicBaseUrl
    web=$newConfig.Server.AllowedOrigins[0]
    config=$configPath
    sqlSecret='DPAPI_LOCAL_MACHINE_PRESERVED'
    internalAuthSecret='DPAPI_LOCAL_MACHINE_PRESERVED'
    authMode=[string]$context.authMode
    serviceMode=[string]$context.serviceMode
    primary=[string]$context.primaryRoot
    backup=[string]$context.backupRoot
    migrations=$true
    safetyRoot=$safetyRoot
    sqlBackup=[string]$context.sqlBackup
    authVerification=$authVerification.status
    verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
  }|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $setupStatePath -Encoding UTF8

  [ordered]@{
    result='SUCCESS'
    completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    version=$ReleaseVersion
    safetyRoot=$safetyRoot
    database=[string]$context.database
    sqlBackup=[string]$context.sqlBackup
    sqlBackupVerified=[bool]$context.sqlBackupVerified
    primaryRoot=[string]$context.primaryRoot
    backupRoot=[string]$context.backupRoot
    apiTcp=$true
    webTcp=$true
    apiHealth=$apiStatus
    webHealth=$webStatus
    authVerification=$authVerification
    authSecretPreserved=$true
    configurationPreserved=$true
  }|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $safetyRoot 'post-upgrade-evidence.json') -Encoding UTF8

  exit 0
}
catch{
  $message=@"
MAM SERVER POST-UPGRADE FAILED
Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Message: $($_.Exception.Message)
Context: $ContextPath
FullError:
$($_ | Out-String)
"@
  try{
    New-Item -ItemType Directory -Force -Path $logRoot|Out-Null
    $message|Set-Content -LiteralPath (Join-Path $logRoot 'upgrade-error.log') -Encoding UTF8
  }catch{}
  try{
    if(Test-Path -LiteralPath $ContextPath){
      $ctx=Get-Content -Raw -LiteralPath $ContextPath|ConvertFrom-Json
      if($ctx.safetyRoot){$message|Set-Content -LiteralPath (Join-Path ([string]$ctx.safetyRoot) 'post-upgrade-error.txt') -Encoding UTF8}
    }
  }catch{}
  Write-Error $_
  exit 1
}
finally{
  $env:MAM_SQL_CONNECTION_STRING=$null
  $sqlPlain=$null
}
