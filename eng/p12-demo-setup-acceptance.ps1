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
    Uri = "http://127.0.0.1:5099$Path"
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
  Assert-True ($demo.VersionInfo.ProductName -eq 'Diwan Al Amiri MAM Demo') 'Demo installer ProductName is incorrect.'

  $manifestPath = Join-Path $SetupRoot 'demo-setup-manifest.json'
  Assert-True (Test-Path -LiteralPath $manifestPath) 'demo-setup-manifest.json is missing.'
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  Assert-True ($manifest.host -eq 'demomam.da.gov.kw') 'Demo manifest hostname is incorrect.'
  Assert-True ($manifest.url -eq 'http://demomam.da.gov.kw/') 'Demo manifest URL is incorrect.'
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

  Set-Phase 'install'
  Invoke-Setup -Exe $demo.FullName -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG={0}" -f $setupLog),("/DIR={0}" -f $installRoot))

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

  Set-Phase 'hosts-and-tasks'
  $hosts = Get-Content -Raw -LiteralPath $hostsPath
  Assert-True ($hosts -match '(?im)^\s*127\.0\.0\.1\s+demomam\.da\.gov\.kw\s+# Diwan MAM Demo\s*$') 'Installer did not map demomam.da.gov.kw to 127.0.0.1.'
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Assert-True ($null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) "Scheduled task missing: $taskName"
  }
  Assert-True ($null -eq (Get-ScheduledTask -TaskName 'Diwan MAM Demo Worker' -ErrorAction SilentlyContinue)) 'Demo must not install a heavyweight Worker task.'

  Set-Phase 'runtime'
  Wait-Url -Uri 'http://127.0.0.1:5099/health/ready' | Out-Null
  Wait-Url -Uri 'http://127.0.0.1:80/version' -Headers @{ Host='demomam.da.gov.kw' } | Out-Null
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
  Wait-Url -Uri 'http://127.0.0.1:5099/health/ready' | Out-Null
  Start-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  Wait-Url -Uri 'http://127.0.0.1:80/version' -Headers @{ Host='demomam.da.gov.kw' } | Out-Null
  $persisted = (Invoke-Api 'GET' "/api/v1/catalog/assets/$assetId").Content | ConvertFrom-Json
  Assert-True ($persisted.title -eq 'Offline Demo Acceptance Asset') 'Catalog data did not persist across API/Web restart.'

  Set-Phase 'web-proxy'
  $webSession = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:80/client-api/session' -Headers @{ Host='demomam.da.gov.kw' } -TimeoutSec 15
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
  Write-Host "P12 offline Demo setup acceptance FAILED in phase [$phase]"
  if (Test-Path -LiteralPath $setupLog) {
    Write-Host '----- demo setup log -----'
    Get-Content -LiteralPath $setupLog -Tail 300 | ForEach-Object { Write-Host $_ }
    Write-Host '----- end demo setup log -----'
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
