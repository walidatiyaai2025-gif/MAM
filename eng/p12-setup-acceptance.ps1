param([Parameter(Mandatory=$true)][string]$SetupRoot)
$ErrorActionPreference='Stop'
$SetupRoot=[IO.Path]::GetFullPath($SetupRoot)
$dataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM'
$work=Join-Path $env:RUNNER_TEMP ('mam-setup-acceptance-'+[Guid]::NewGuid().ToString('N'))
$desktopDir=Join-Path $work 'Desktop'; $serverDir=Join-Path $work 'Server'; $primary=Join-Path $work 'Primary'; $backup=Join-Path $work 'Backup'
New-Item -ItemType Directory -Force -Path $work | Out-Null
Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue

function Run-Setup([string]$Exe,[string[]]$Arguments) {
  $p=Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "Setup failed: $([IO.Path]::GetFileName($Exe)) exit=$($p.ExitCode)" }
}
function Assert([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }

try {
  $desktop=@(Get-ChildItem -LiteralPath $SetupRoot -Filter 'DiwanMAM-Desktop-Setup-*-x64.exe')
  $server=@(Get-ChildItem -LiteralPath $SetupRoot -Filter 'DiwanMAM-Server-Setup-*-x64.exe')
  Assert ($desktop.Count -eq 1) "Expected exactly one Desktop Setup EXE; found $($desktop.Count)."
  Assert ($server.Count -eq 1) "Expected exactly one Server Setup EXE; found $($server.Count)."
  $desktop=$desktop[0]; $server=$server[0]

  foreach($setup in @($desktop,$server)) {
    $info=$setup.VersionInfo
    Assert ($info.CompanyName -eq 'Diwan Al Amiri') "Setup CompanyName is not Diwan Al Amiri: $($setup.Name)"
    Assert ($info.ProductName -like 'Diwan Al Amiri MAM*') "Setup ProductName is not branded: $($setup.Name)"
  }

  Run-Setup $desktop.FullName @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$desktopDir",'/APIURL=http://127.0.0.1:5080',('/CAPTURECACHE='+[Environment]::ExpandEnvironmentVariables('%LOCALAPPDATA%\Diwan Al Amiri\MAM\CaptureCache')))
  Assert (Test-Path (Join-Path $desktopDir 'MAM.Desktop.exe')) 'Desktop executable was not installed.'
  $desktopConfig=Join-Path $dataRoot 'desktop.setup.json'
  Assert (Test-Path $desktopConfig) 'Desktop installer did not create managed configuration.'
  $dc=Get-Content -Raw $desktopConfig | ConvertFrom-Json
  Assert ($dc.apiBaseUrl -eq 'http://127.0.0.1:5080') 'Desktop API URL was not persisted by Setup.'
  Assert (-not [string]::IsNullOrWhiteSpace($dc.captureCacheRoot)) 'Desktop capture cache was not persisted.'
  Run-Setup (Join-Path $desktopDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
  Assert (Test-Path $desktopConfig) 'Desktop uninstall must preserve managed configuration for reinstall/upgrade.'

  $sqlInput=Join-Path $work 'sql.secret.input'
  $fakeSql='Server=127.0.0.1;Initial Catalog=MamSetupAcceptance;User Id=setup_user;Password=SetupOnly-NotARealSecret!;TrustServerCertificate=True'
  Set-Content -LiteralPath $sqlInput -Value $fakeSql -NoNewline -Encoding UTF8
  Run-Setup $server.FullName @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$serverDir",'/ENVIRONMENT=UAT','/PUBLICHOST=127.0.0.1','/APIPORT=15080','/WEBPORT=15481',"/SQLSECRETFILE=$sqlInput",("/PRIMARYROOT=$primary"),("/BACKUPROOT=$backup"),'/BACKUPPOLICY=SETUP-ACCEPTANCE','/AUTHMODE=Local','/RETENTIONDAYS=30','/AUDITRETENTIONDAYS=365','/AUDITREADPOLICY=MetadataAndDownloads','/SERVICEMODE=System','/APPLYMIGRATIONS=0','/OPENFIREWALL=0','/STARTSERVICES=0')

  foreach($relative in @('api\MAM.Api.exe','web\MAM.Web.exe','worker\MAM.Worker.exe','sql\tool\MAM.Deployment.exe','setup\Start-MamComponent.ps1')) {
    Assert (Test-Path (Join-Path $serverDir $relative)) "Server installer missing $relative"
  }
  $serverConfig=Join-Path $dataRoot 'config\appsettings.Production.json'
  $sqlProtected=Join-Path $dataRoot 'secrets\sql.connection.dpapi'
  Assert (Test-Path $serverConfig) 'Server Setup did not generate managed configuration.'
  Assert (Test-Path $sqlProtected) 'Server Setup did not create DPAPI SQL secret.'
  $raw=Get-Content -Raw $serverConfig
  Assert ($raw -notmatch 'SetupOnly-NotARealSecret|User Id=setup_user|Server=127.0.0.1') 'Plaintext SQL secret leaked into generated configuration.'
  $sc=$raw | ConvertFrom-Json
  Assert ($sc.Database.ConnectionStringSecretRef -eq 'env:MAM_SQL_CONNECTION_STRING') 'Generated configuration does not use a secret reference.'
  Assert ($sc.Storage.Primary.Root -eq $primary) 'Primary storage root was not setup-managed.'
  Assert ($sc.Storage.Backup.Root -eq $backup) 'Backup storage root was not setup-managed.'
  Assert ($sc.Brand.OrganizationNameEn -eq 'Diwan Al Amiri') 'Generated server configuration lost Diwan branding.'
  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')) { Assert ($null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) "Scheduled task missing: $taskName" }

  Run-Setup (Join-Path $serverDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')) { Assert ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) "Scheduled task survived uninstall: $taskName" }
  Assert (Test-Path $serverConfig) 'Server uninstall must preserve configuration/data by default.'

  $brand=Get-Content -Raw (Join-Path $SetupRoot 'brand-manifest.json') | ConvertFrom-Json
  Assert ($brand.sha256 -eq 'bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb') 'Installer branding does not originate from the approved Diwan crest fingerprint.'
  $manifest=Get-Content -Raw (Join-Path $SetupRoot 'setup-manifest.json') | ConvertFrom-Json
  Assert ($manifest.artifacts.Count -eq 2) 'Setup manifest must contain exactly two Setup EXEs.'
  foreach($artifact in $manifest.artifacts) {
    $path=Join-Path $SetupRoot $artifact.file
    Assert ((Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() -eq $artifact.sha256) "Setup SHA-256 mismatch: $($artifact.file)"
  }
  Write-Host 'P12 premium dual-setup acceptance: SUCCESS'
}
finally {
  foreach($taskName in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue }
  Remove-NetFirewallRule -DisplayName 'Diwan MAM API' -ErrorAction SilentlyContinue
  Remove-NetFirewallRule -DisplayName 'Diwan MAM Web' -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $dataRoot,$work -Recurse -Force -ErrorAction SilentlyContinue
}
