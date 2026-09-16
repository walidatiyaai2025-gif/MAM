param(
  [Parameter(Mandatory = $true)]
  [string] $SetupRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SetupRoot = [IO.Path]::GetFullPath($SetupRoot)
$work = Join-Path $env:RUNNER_TEMP ('mam-demo-acceptance-' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $work 'Install'
$setupLog = Join-Path $work 'demo-setup.log'
$dataRoot = Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Demo'
$dbPath = Join-Path $dataRoot 'database\mam-demo.db'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$originalHosts = if (Test-Path -LiteralPath $hostsPath) { Get-Content -Raw -LiteralPath $hostsPath } else { '' }
$phase = 'initialization'
$demoHost = 'demomam.da.gov.kw'
$apiPort = 15099
$webPort = 18080
$demoOrigin = "http://$demoHost`:$webPort"

New-Item -ItemType Directory -Force -Path $work | Out-Null

function Set-Phase([string]$Name) {
  $script:phase = $Name
  Write-Host "========== P12 DEMO PHASE: $Name =========="
}

function Assert-True([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw "[$script:phase] $Message" }
}

function Invoke-Setup([string]$Exe,[string[]]$Arguments) {
  $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw "[$script:phase] Setup failed: $([IO.Path]::GetFileName($Exe)) exit=$($process.ExitCode)" }
}

function Invoke-Api([string]$Method,[string]$Path,[object]$Body=$null) {
  $headers = @{ 'X-MAM-Dev-User' = 'admin' }
  $parameters = @{
    Method = $Method
    Uri = "http://127.0.0.1:$apiPort$Path"
    Headers = $headers
    UseBasicParsing = $true
    TimeoutSec = 15
  }
  if ($null -ne $Body) {
    $parameters.ContentType = 'application/json'
    $parameters.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
  }
  return Invoke-WebRequest @parameters
}

function Wait-Url([string]$Uri,[hashtable]$Headers=@{},[int]$Attempts=40) {
  for ($i=0; $i -lt $Attempts; $i++) {
    try {
      $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Headers $Headers -TimeoutSec 2
      if ($response.StatusCode -eq 200) { return $response }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  throw "[$script:phase] URL did not become ready: $Uri"
}

try {
  Set-Phase 'discover-artifact'
  $candidates = @(Get-ChildItem -LiteralPath $SetupRoot -Filter 'DiwanMAM-Demo-Setup-*-x64.exe')
  Assert-True ($candidates.Count -eq 1) "Expected exactly one Demo Setup EXE; found $($candidates.Count)."
  $demo = $candidates[0]
  Assert-True (([string]$demo.VersionInfo.CompanyName).Trim() -eq 'Diwan Al Amiri') 'Demo installer CompanyName is incorrect.'
  Assert-True (([string]$demo.VersionInfo.ProductName).Trim() -eq 'Diwan Al Amiri MAM Demo') 'Demo installer ProductName is incorrect.'

  $manifestPath = Join-Path $SetupRoot 'demo-setup-manifest.json'
  Assert-True (Test-Path -LiteralPath $manifestPath) 'demo-setup-manifest.json is missing.'
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  Assert-True ($manifest.host -eq $demoHost) 'Demo manifest hostname is incorrect.'
  Assert-True ($manifest.url -eq 'http://demomam.da.gov.kw/') 'Demo manifest must preserve the production demo URL on default port 80.'
  Assert-True ($manifest.database -eq 'SQLite') 'Demo manifest must declare SQLite.'
  Assert-True ($manifest.internetRequired -eq $false) 'Demo manifest must declare Internet is not required.'
  Assert-True ($manifest.sqlServerRequired -eq $false) 'Demo manifest must declare SQL Server is not required.'
  $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $demo.FullName).Hash.ToLowerInvariant()
  Assert-True ($actualHash -eq $manifest.artifact.sha256) 'Demo Setup SHA-256 does not match its manifest.'

  Set-Phase 'clean-host'
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
  foreach ($port in @($apiPort,$webPort)) {
    Assert-True ($null -eq (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1)) "Acceptance port $port is already occupied on the runner."
  }

  Set-Phase 'install'
  Invoke-Setup -Exe $demo.FullName -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG={0}" -f $setupLog),("/DIR={0}" -f $installRoot),("/APIPORT={0}" -f $apiPort),("/WEBPORT={0}" -f $webPort))

  Set-Phase 'installed-files'
  foreach ($relative in @('api\MAM.Api.exe','web\MAM.Web.exe','config\appsettings.Demo.template.json','setup\Configure-MamDemo.ps1','setup\Start-MamDemoComponent.ps1')) {
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot $relative)) "Demo installer missing $relative"
  }
  Assert-True (Test-Path -LiteralPath $dbPath) 'Embedded SQLite database was not created.'
  $runtimeConfigPath = Join-Path $dataRoot 'config\appsettings.Demo.json'
  Assert-True (Test-Path -LiteralPath $runtimeConfigPath) 'Runtime Demo configuration was not created.'
  $runtimeConfig = Get-Content -Raw -LiteralPath $runtimeConfigPath | ConvertFrom-Json
  Assert-True ($runtimeConfig.Environment.Name -eq 'Demo') 'Runtime environment is not Demo.'
  Assert-True ($runtimeConfig.Database.Provider -eq 'Sqlite') 'Runtime database provider is not Sqlite.'
  Assert-True ($runtimeConfig.Database.SqlitePath -eq $dbPath) 'Runtime SQLite path is incorrect.'
  Assert-True ([string]::IsNullOrWhiteSpace([string]$runtimeConfig.Database.ConnectionStringSecretRef)) 'Demo configuration must not require a SQL Server connection string.'
  Assert-True ($runtimeConfig.Server.PublicBaseUrl -eq "http://127.0.0.1:$apiPort") 'Runtime API base URL does not honor the isolated acceptance API port.'
  Assert-True ($runtimeConfig.Server.AllowedOrigins -contains $demoOrigin) 'Runtime allowed origin does not honor the isolated acceptance Web port.'
  $setupStatePath = Join-Path $dataRoot 'setup-state.json'
  Assert-True (Test-Path -LiteralPath $setupStatePath) 'Demo setup-state.json is missing.'
  $setupState = Get-Content -Raw -LiteralPath $setupStatePath | ConvertFrom-Json
  Assert-True ($setupState.url -eq "$demoOrigin/") 'Demo setup state did not persist the effective Web URL.'

  Set-Phase 'hosts-and-tasks'
  $hosts = Get-Content -Raw -LiteralPath $hostsPath
  Assert-True ($hosts -match '(?im)^\s*127\.0\.0\.1\s+demomam\.da\.gov\.kw\s+# Diwan MAM Demo\s*$') 'Installer did not map demomam.da.gov.kw to 127.0.0.1.'
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Assert-True ($null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) "Scheduled task missing: $taskName"
  }
  Assert-True ($null -eq (Get-ScheduledTask -TaskName 'Diwan MAM Demo Worker' -ErrorAction SilentlyContinue)) 'Demo must not install a heavyweight Worker task.'

  Set-Phase 'runtime'
  Wait-Url -Uri "http://127.0.0.1:$apiPort/health/ready" | Out-Null
  Wait-Url -Uri "http://127.0.0.1:$webPort/version" -Headers @{ Host=$demoHost } | Out-Null
  $session = (Invoke-Api 'GET' '/api/v1/session').Content | ConvertFrom-Json
  Assert-True ($session.userId -eq 'demo-admin') 'Demo API did not authenticate the built-in demo administrator.'
  Assert-True ($session.roles -contains 'Administrator') 'Demo administrator role is missing.'

  Set-Phase 'sqlite-persistence'
  $created = (Invoke-Api 'POST' '/api/v1/catalog/assets' @{ title='Offline Demo Acceptance Asset' }).Content | ConvertFrom-Json
  $assetId = [string]$created.id
  Assert-True (-not [string]::IsNullOrWhiteSpace($assetId)) 'Demo asset creation did not return an ID.'
  Assert-True ((Get-Item -LiteralPath $dbPath).Length -gt 0) 'SQLite database file is empty after a catalog mutation.'

  Stop-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  Stop-ScheduledTask -TaskName 'Diwan MAM Demo API'
  Start-Sleep -Seconds 1
  Start-ScheduledTask -TaskName 'Diwan MAM Demo API'
  Wait-Url -Uri "http://127.0.0.1:$apiPort/health/ready" | Out-Null
  Start-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  Wait-Url -Uri "http://127.0.0.1:$webPort/version" -Headers @{ Host=$demoHost } | Out-Null
  $persisted = (Invoke-Api 'GET' "/api/v1/catalog/assets/$assetId").Content | ConvertFrom-Json
  Assert-True ($persisted.title -eq 'Offline Demo Acceptance Asset') 'Catalog data did not persist across API/Web restart.'

  Set-Phase 'web-proxy'
  $webSession = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$webPort/client-api/session" -Headers @{ Host=$demoHost } -TimeoutSec 15
  Assert-True ($webSession.StatusCode -eq 200) 'Demo Web did not proxy to the local API.'
  $webSessionJson = $webSession.Content | ConvertFrom-Json
  Assert-True ($webSessionJson.userId -eq 'demo-admin') 'Demo Web proxy did not use the local demo identity.'

  Set-Phase 'uninstall-preservation'
  Invoke-Setup -Exe (Join-Path $installRoot 'unins000.exe') -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Assert-True ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) "Demo task survived uninstall: $taskName"
  }
  $hostsAfter = Get-Content -Raw -LiteralPath $hostsPath
  Assert-True ($hostsAfter -notmatch '# Diwan MAM Demo') 'Demo hosts mapping survived uninstall.'
  Assert-True (Test-Path -LiteralPath $dbPath) 'Demo uninstall must preserve local demo data by default.'

  Set-Phase 'success'
  Write-Host 'P12 offline Demo setup acceptance: SUCCESS'
}
catch {
  $failure = $_
  Write-Host "P12 offline Demo setup acceptance FAILED in phase [$phase]"
  Write-Host ($failure | Out-String)
  if ($failure.ScriptStackTrace) { Write-Host $failure.ScriptStackTrace }

  $diagnosticRoot = Join-Path $SetupRoot '_acceptance-diagnostics'
  New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
  @(
    "phase=$phase",
    "message=$($failure.Exception.Message)",
    "scriptStack=$($failure.ScriptStackTrace)",
    "installRoot=$installRoot",
    "dataRoot=$dataRoot",
    "apiPort=$apiPort",
    "webPort=$webPort"
  ) | Set-Content -LiteralPath (Join-Path $diagnosticRoot 'demo-failure-state.txt') -Encoding UTF8

  if (Test-Path -LiteralPath $setupLog) {
    Copy-Item -LiteralPath $setupLog -Destination (Join-Path $diagnosticRoot 'demo-setup.log') -Force
    Write-Host '----- demo setup log -----'
    Get-Content -LiteralPath $setupLog -Tail 300 | ForEach-Object { Write-Host $_ }
    Write-Host '----- end demo setup log -----'
  }

  $runtimeLogRoot = Join-Path $dataRoot 'logs'
  if (Test-Path -LiteralPath $runtimeLogRoot) {
    Get-ChildItem -LiteralPath $runtimeLogRoot -File -ErrorAction SilentlyContinue | ForEach-Object {
      Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $diagnosticRoot $_.Name) -Force
      Write-Host "----- $($_.Name) -----"
      Get-Content -LiteralPath $_.FullName -Tail 300 | ForEach-Object { Write-Host $_ }
      Write-Host "----- end $($_.Name) -----"
    }
  }

  $runtimeConfigPath = Join-Path $dataRoot 'config\appsettings.Demo.json'
  if (Test-Path -LiteralPath $runtimeConfigPath) {
    Copy-Item -LiteralPath $runtimeConfigPath -Destination (Join-Path $diagnosticRoot 'appsettings.Demo.json') -Force
  }

  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    try {
      $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
      $info = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction Stop
      @(
        "TaskName=$taskName",
        "State=$($task.State)",
        "LastRunTime=$($info.LastRunTime.ToString('O'))",
        "LastTaskResult=$($info.LastTaskResult)",
        "NextRunTime=$($info.NextRunTime.ToString('O'))"
      ) | Set-Content -LiteralPath (Join-Path $diagnosticRoot (($taskName -replace '[^A-Za-z0-9]+','-') + '.txt')) -Encoding UTF8
    } catch {
      "TaskName=$taskName`nUnavailable=$($_.Exception.Message)" | Set-Content -LiteralPath (Join-Path $diagnosticRoot (($taskName -replace '[^A-Za-z0-9]+','-') + '.txt')) -Encoding UTF8
    }
  }

  try {
    Get-NetTCPConnection -State Listen -ErrorAction Stop |
      Where-Object { $_.LocalPort -in @($webPort,$apiPort) } |
      Format-List * |
      Out-String |
      Set-Content -LiteralPath (Join-Path $diagnosticRoot 'demo-listeners.txt') -Encoding UTF8
  } catch {
    "Listener diagnostics unavailable: $($_.Exception.Message)" | Set-Content -LiteralPath (Join-Path $diagnosticRoot 'demo-listeners.txt') -Encoding UTF8
  }
  throw
}
finally {
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  try { Set-Content -LiteralPath $hostsPath -Value $originalHosts -NoNewline -Encoding ASCII -Force; ipconfig /flushdns | Out-Null } catch { Write-Host "Hosts restore warning: $($_.Exception.Message)" }
  Remove-Item -LiteralPath $dataRoot,$work -Recurse -Force -ErrorAction SilentlyContinue
}