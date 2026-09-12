param(
  [Parameter(Mandatory = $true)]
  [string] $SetupRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$SetupRoot = [IO.Path]::GetFullPath($SetupRoot)
$dataRoot = Join-Path $env:ProgramData "Diwan Al Amiri\MAM"
$work = Join-Path $env:RUNNER_TEMP ("mam-setup-acceptance-" + [Guid]::NewGuid().ToString("N"))
$desktopDir = Join-Path $work "Desktop"
$serverDir = Join-Path $work "Server"
$primary = Join-Path $work "Primary"
$backup = Join-Path $work "Backup"
$desktopSetupLog = Join-Path $work "desktop-setup.log"
$serverSetupLog = Join-Path $work "server-setup.log"
$diagnosticRoot = Join-Path $SetupRoot "_acceptance-diagnostics"
$transcriptPath = Join-Path $diagnosticRoot "acceptance-transcript.log"
$phase = "initialization"
$transcriptStarted = $false

New-Item -ItemType Directory -Force -Path $work | Out-Null
Remove-Item -LiteralPath $diagnosticRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue

try {
  Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null
  $transcriptStarted = $true
} catch {
  Write-Host ("Unable to start transcript; continuing with console diagnostics: {0}" -f $_.Exception.Message)
}

function Set-Phase {
  param([Parameter(Mandatory = $true)][string] $Name)
  $script:phase = $Name
  Write-Host ("========== P12 SETUP PHASE: {0} ==========" -f $Name)
}

function Invoke-Setup {
  param(
    [Parameter(Mandatory = $true)][string] $Exe,
    [Parameter(Mandatory = $true)][string[]] $Arguments
  )

  Write-Host ("Launching {0}" -f $Exe)
  Write-Host ("Arguments: {0}" -f ($Arguments -join ' '))
  $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru
  Write-Host ("Exit code: {0}" -f $process.ExitCode)
  if ($process.ExitCode -ne 0) {
    throw ("Setup failed during phase [{0}]: {1} exit={2}" -f $script:phase, [IO.Path]::GetFileName($Exe), $process.ExitCode)
  }
}

function Assert-True {
  param(
    [Parameter(Mandatory = $true)][bool] $Condition,
    [Parameter(Mandatory = $true)][string] $Message
  )

  if (-not $Condition) {
    throw ("[{0}] {1}" -f $script:phase, $Message)
  }
}

function Assert-PathWithDiagnostics {
  param(
    [Parameter(Mandatory = $true)][string] $Path,
    [Parameter(Mandatory = $true)][string] $Message,
    [string] $DiagnosticLog = ''
  )

  if (Test-Path -LiteralPath $Path) { return }
  if (-not [string]::IsNullOrWhiteSpace($DiagnosticLog) -and (Test-Path -LiteralPath $DiagnosticLog)) {
    Write-Host "----- setup diagnostic log -----"
    Get-Content -LiteralPath $DiagnosticLog -Tail 250 | ForEach-Object { Write-Host $_ }
    Write-Host "----- end setup diagnostic log -----"
  }
  throw ("[{0}] {1}" -f $script:phase, $Message)
}

function Save-DiagnosticSnapshot {
  param([string] $Reason)

  try {
    $snapshotPath = Join-Path $diagnosticRoot "state.txt"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("phase=$script:phase")
    $lines.Add("reason=$Reason")
    $lines.Add("desktopDir.exists=$(Test-Path -LiteralPath $desktopDir)")
    $lines.Add("serverDir.exists=$(Test-Path -LiteralPath $serverDir)")
    $lines.Add("desktopExe.exists=$(Test-Path -LiteralPath (Join-Path $desktopDir 'MAM.Desktop.exe'))")
    $lines.Add("desktopConfig.exists=$(Test-Path -LiteralPath (Join-Path $dataRoot 'desktop.setup.json'))")
    $lines.Add("serverConfig.exists=$(Test-Path -LiteralPath (Join-Path $dataRoot 'config\appsettings.Production.json'))")
    $lines.Add("sqlDpapi.exists=$(Test-Path -LiteralPath (Join-Path $dataRoot 'secrets\sql.connection.dpapi'))")
    $lines.Add("primary.exists=$(Test-Path -LiteralPath $primary)")
    $lines.Add("backup.exists=$(Test-Path -LiteralPath $backup)")
    foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
      $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
      $lines.Add(("task.{0}.exists={1}" -f $taskName, ($null -ne $task)))
      if ($null -ne $task) { $lines.Add(("task.{0}.state={1}" -f $taskName, $task.State)) }
    }
    $lines | Set-Content -LiteralPath $snapshotPath -Encoding UTF8
  } catch {
    Write-Host ("Diagnostic snapshot failed: {0}" -f $_.Exception.Message)
  }

  foreach ($pair in @(
    @($desktopSetupLog, (Join-Path $diagnosticRoot 'desktop-setup.log')),
    @($serverSetupLog, (Join-Path $diagnosticRoot 'server-setup.log'))
  )) {
    try {
      if (Test-Path -LiteralPath $pair[0]) { Copy-Item -LiteralPath $pair[0] -Destination $pair[1] -Force }
    } catch {
      Write-Host ("Unable to preserve setup log {0}: {1}" -f $pair[0], $_.Exception.Message)
    }
  }
}

try {
  Set-Phase "discover-and-brand-validate"
  $desktopCandidates = @(Get-ChildItem -LiteralPath $SetupRoot -Filter "DiwanMAM-Desktop-Setup-*-x64.exe")
  $serverCandidates = @(Get-ChildItem -LiteralPath $SetupRoot -Filter "DiwanMAM-Server-Setup-*-x64.exe")

  Assert-True ($desktopCandidates.Count -eq 1) ("Expected exactly one Desktop Setup EXE; found {0}." -f $desktopCandidates.Count)
  Assert-True ($serverCandidates.Count -eq 1) ("Expected exactly one Server Setup EXE; found {0}." -f $serverCandidates.Count)

  $desktop = $desktopCandidates[0]
  $server = $serverCandidates[0]

  foreach ($setup in @($desktop, $server)) {
    $info = $setup.VersionInfo
    $companyName = ([string]$info.CompanyName).Trim()
    Assert-True ($companyName -eq "Diwan Al Amiri") ("Setup CompanyName is not Diwan Al Amiri: {0}; actual=[{1}]" -f $setup.Name, $companyName)
    Assert-True ($info.ProductName -like "Diwan Al Amiri MAM*") ("Setup ProductName is not branded: {0}" -f $setup.Name)
  }

  Set-Phase "desktop-install"
  $captureCache = [Environment]::ExpandEnvironmentVariables("%LOCALAPPDATA%\Diwan Al Amiri\MAM\CaptureCache")
  $desktopArguments = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    ("/LOG={0}" -f $desktopSetupLog),
    ("/DIR={0}" -f $desktopDir),
    "/APIURL=http://127.0.0.1:5080",
    ("/CAPTURECACHE={0}" -f $captureCache)
  )
  Invoke-Setup -Exe $desktop.FullName -Arguments $desktopArguments

  Set-Phase "desktop-assertions"
  Assert-True (Test-Path (Join-Path $desktopDir "MAM.Desktop.exe")) "Desktop executable was not installed."
  $desktopConfig = Join-Path $dataRoot "desktop.setup.json"
  Assert-True (Test-Path $desktopConfig) "Desktop installer did not create managed configuration."

  $desktopConfiguration = Get-Content -Raw $desktopConfig | ConvertFrom-Json
  Assert-True ($desktopConfiguration.apiBaseUrl -eq "http://127.0.0.1:5080") "Desktop API URL was not persisted by Setup."
  Assert-True (-not [string]::IsNullOrWhiteSpace($desktopConfiguration.captureCacheRoot)) "Desktop capture cache was not persisted."

  Set-Phase "desktop-uninstall-preservation"
  Invoke-Setup -Exe (Join-Path $desktopDir "unins000.exe") -Arguments @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
  Assert-True (Test-Path $desktopConfig) "Desktop uninstall must preserve managed configuration for reinstall/upgrade."

  Set-Phase "server-install"
  $sqlInput = Join-Path $work "sql.secret.input"
  $testConnectionMaterial = "Data Source=127.0.0.1;Initial Catalog=MamSetupAcceptance;Integrated Security=True;TrustServerCertificate=True"
  Set-Content -LiteralPath $sqlInput -Value $testConnectionMaterial -NoNewline -Encoding UTF8

  $serverArguments = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    ("/LOG={0}" -f $serverSetupLog),
    ("/DIR={0}" -f $serverDir),
    "/ENVIRONMENT=UAT",
    "/PUBLICHOST=127.0.0.1",
    "/APIPORT=15080",
    "/WEBPORT=15481",
    ("/SQLSECRETFILE={0}" -f $sqlInput),
    ("/PRIMARYROOT={0}" -f $primary),
    ("/BACKUPROOT={0}" -f $backup),
    "/BACKUPPOLICY=SETUP-ACCEPTANCE",
    "/AUTHMODE=Local",
    "/RETENTIONDAYS=30",
    "/AUDITRETENTIONDAYS=365",
    "/AUDITREADPOLICY=MetadataAndDownloads",
    "/SERVICEMODE=System",
    "/APPLYMIGRATIONS=0",
    "/OPENFIREWALL=0",
    "/STARTSERVICES=0"
  )
  Invoke-Setup -Exe $server.FullName -Arguments $serverArguments

  Set-Phase "server-file-and-config-assertions"
  foreach ($relative in @(
    "api\MAM.Api.exe",
    "web\MAM.Web.exe",
    "worker\MAM.Worker.exe",
    "sql\tool\MAM.Deployment.exe",
    "setup\Start-MamComponent.ps1"
  )) {
    Assert-True (Test-Path (Join-Path $serverDir $relative)) ("Server installer missing {0}" -f $relative)
  }

  $serverConfig = Join-Path $dataRoot "config\appsettings.Production.json"
  $sqlProtected = Join-Path $dataRoot "secrets\sql.connection.dpapi"
  Assert-PathWithDiagnostics -Path $serverConfig -Message "Server Setup did not generate managed configuration." -DiagnosticLog $serverSetupLog
  Assert-PathWithDiagnostics -Path $sqlProtected -Message "Server Setup did not create DPAPI SQL secret." -DiagnosticLog $serverSetupLog

  $raw = Get-Content -Raw $serverConfig
  Assert-True ($raw -notmatch "MamSetupAcceptance|Data Source=127.0.0.1") "Plaintext SQL connection material leaked into generated configuration."

  $serverConfiguration = $raw | ConvertFrom-Json
  Assert-True ($serverConfiguration.Database.ConnectionStringSecretRef -eq "env:MAM_SQL_CONNECTION_STRING") "Generated configuration does not use a secret reference."
  Assert-True ($serverConfiguration.Storage.Primary.Root -eq $primary) "Primary storage root was not setup-managed."
  Assert-True ($serverConfiguration.Storage.Backup.Root -eq $backup) "Backup storage root was not setup-managed."
  Assert-True ($serverConfiguration.Brand.OrganizationNameEn -eq "Diwan Al Amiri") "Generated server configuration lost Diwan branding."

  Set-Phase "server-task-assertions"
  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Assert-True ($null -ne $task) ("Scheduled task missing: {0}" -f $taskName)
  }

  Set-Phase "server-uninstall-preservation"
  Invoke-Setup -Exe (Join-Path $serverDir "unins000.exe") -Arguments @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")

  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Assert-True ($null -eq $task) ("Scheduled task survived uninstall: {0}" -f $taskName)
  }
  Assert-True (Test-Path $serverConfig) "Server uninstall must preserve configuration/data by default."

  Set-Phase "manifest-and-hash-assertions"
  $brand = Get-Content -Raw (Join-Path $SetupRoot "brand-manifest.json") | ConvertFrom-Json
  Assert-True ($brand.sha256 -eq "bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb") "Installer branding does not originate from the approved Diwan crest fingerprint."

  $manifest = Get-Content -Raw (Join-Path $SetupRoot "setup-manifest.json") | ConvertFrom-Json
  Assert-True ($manifest.artifacts.Count -eq 2) "Setup manifest must contain exactly two Setup EXEs."
  foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $SetupRoot $artifact.file
    $actualHash = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    Assert-True ($actualHash -eq $artifact.sha256) ("Setup SHA-256 mismatch: {0}" -f $artifact.file)
  }

  Set-Phase "success"
  Save-DiagnosticSnapshot -Reason "success"
  Write-Host "P12 premium dual-setup acceptance: SUCCESS"
}
catch {
  Write-Host ("P12 premium dual-setup acceptance FAILED in phase [{0}]" -f $phase)
  Write-Host ("ERROR: {0}" -f $_.Exception.Message)
  Save-DiagnosticSnapshot -Reason $_.Exception.Message
  throw
}
finally {
  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  Remove-NetFirewallRule -DisplayName "Diwan MAM API" -ErrorAction SilentlyContinue
  Remove-NetFirewallRule -DisplayName "Diwan MAM Web" -ErrorAction SilentlyContinue
  Save-DiagnosticSnapshot -Reason ("finalize:{0}" -f $phase)
  if ($transcriptStarted) {
    try { Stop-Transcript | Out-Null } catch { Write-Host ("Unable to stop transcript: {0}" -f $_.Exception.Message) }
  }
  Remove-Item -LiteralPath $dataRoot, $work -Recurse -Force -ErrorAction SilentlyContinue
}
