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
$serverSetupLog = Join-Path $work "server-setup.log"

New-Item -ItemType Directory -Force -Path $work | Out-Null
Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue

function Invoke-Setup {
  param(
    [Parameter(Mandatory = $true)][string] $Exe,
    [Parameter(Mandatory = $true)][string[]] $Arguments
  )

  $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru
  if ($process.ExitCode -ne 0) {
    throw ("Setup failed: {0} exit={1}" -f [IO.Path]::GetFileName($Exe), $process.ExitCode)
  }
}

function Assert-True {
  param(
    [Parameter(Mandatory = $true)][bool] $Condition,
    [Parameter(Mandatory = $true)][string] $Message
  )

  if (-not $Condition) {
    throw $Message
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
  throw $Message
}

try {
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

  $captureCache = [Environment]::ExpandEnvironmentVariables("%LOCALAPPDATA%\Diwan Al Amiri\MAM\CaptureCache")
  $desktopArguments = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    ("/DIR={0}" -f $desktopDir),
    "/APIURL=http://127.0.0.1:5080",
    ("/CAPTURECACHE={0}" -f $captureCache)
  )
  Invoke-Setup -Exe $desktop.FullName -Arguments $desktopArguments

  Assert-True (Test-Path (Join-Path $desktopDir "MAM.Desktop.exe")) "Desktop executable was not installed."
  $desktopConfig = Join-Path $dataRoot "desktop.setup.json"
  Assert-True (Test-Path $desktopConfig) "Desktop installer did not create managed configuration."

  $desktopConfiguration = Get-Content -Raw $desktopConfig | ConvertFrom-Json
  Assert-True ($desktopConfiguration.apiBaseUrl -eq "http://127.0.0.1:5080") "Desktop API URL was not persisted by Setup."
  Assert-True (-not [string]::IsNullOrWhiteSpace($desktopConfiguration.captureCacheRoot)) "Desktop capture cache was not persisted."

  Invoke-Setup -Exe (Join-Path $desktopDir "unins000.exe") -Arguments @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
  Assert-True (Test-Path $desktopConfig) "Desktop uninstall must preserve managed configuration for reinstall/upgrade."

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

  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Assert-True ($null -ne $task) ("Scheduled task missing: {0}" -f $taskName)
  }

  Invoke-Setup -Exe (Join-Path $serverDir "unins000.exe") -Arguments @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")

  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Assert-True ($null -eq $task) ("Scheduled task survived uninstall: {0}" -f $taskName)
  }
  Assert-True (Test-Path $serverConfig) "Server uninstall must preserve configuration/data by default."

  $brand = Get-Content -Raw (Join-Path $SetupRoot "brand-manifest.json") | ConvertFrom-Json
  Assert-True ($brand.sha256 -eq "bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb") "Installer branding does not originate from the approved Diwan crest fingerprint."

  $manifest = Get-Content -Raw (Join-Path $SetupRoot "setup-manifest.json") | ConvertFrom-Json
  Assert-True ($manifest.artifacts.Count -eq 2) "Setup manifest must contain exactly two Setup EXEs."
  foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $SetupRoot $artifact.file
    $actualHash = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    Assert-True ($actualHash -eq $artifact.sha256) ("Setup SHA-256 mismatch: {0}" -f $artifact.file)
  }

  Write-Host "P12 premium dual-setup acceptance: SUCCESS"
}
finally {
  foreach ($taskName in @("Diwan MAM API", "Diwan MAM Web", "Diwan MAM Worker")) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  Remove-NetFirewallRule -DisplayName "Diwan MAM API" -ErrorAction SilentlyContinue
  Remove-NetFirewallRule -DisplayName "Diwan MAM Web" -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $dataRoot, $work -Recurse -Force -ErrorAction SilentlyContinue
}
