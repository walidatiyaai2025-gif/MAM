param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [string]$DemoHost='demomam.da.gov.kw',
  [int]$ApiPort=5099,
  [int]$WebPort=80,
  [int]$StartServices=1
)
$ErrorActionPreference='Stop'

$dataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Demo'
$logRoot=Join-Path $dataRoot 'logs'
try { New-Item -ItemType Directory -Force -Path $logRoot | Out-Null } catch { }
trap {
  $failure=$_
  try {
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    @(
      "timestamp=$([DateTimeOffset]::UtcNow.ToString('O'))",
      "message=$($failure.Exception.Message)",
      "scriptStack=$($failure.ScriptStackTrace)",
      ($failure | Out-String)
    ) | Set-Content -LiteralPath (Join-Path $logRoot 'configure-demo-error.log') -Encoding UTF8
  } catch { }
  [Console]::Error.WriteLine($failure.Exception.Message)
  exit 1
}

function Assert-Admin {
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
  $principal=New-Object Security.Principal.WindowsPrincipal($identity)
  if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'MAM Demo setup must run as Administrator.'}
}
function Register-DemoTask([string]$Name,[string]$Component,[string]$ConfigPath){
  $runner=Join-Path $InstallRoot 'setup\Start-MamDemoComponent.ps1'
  $args="-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runner`" -Component $Component -InstallRoot `"$InstallRoot`" -ConfigPath `"$ConfigPath`" -ApiPort $ApiPort -WebPort $WebPort"
  $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $args
  $trigger=New-ScheduledTaskTrigger -AtStartup
  $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
  $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
  Unregister-ScheduledTask -TaskName $Name -Confirm:$false -ErrorAction SilentlyContinue
  Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -Principal $principal | Out-Null
}
function Set-DemoHostsEntry([string]$HostName){
  $hosts=Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
  $tag='# Diwan MAM Demo'
  $lines=@()
  if(Test-Path -LiteralPath $hosts){$lines=Get-Content -LiteralPath $hosts | Where-Object {$_ -notmatch [regex]::Escape($tag) -and $_ -notmatch "(?i)^\s*(127\.0\.0\.1|::1)\s+$([regex]::Escape($HostName))(\s|$)"}}
  $lines += "127.0.0.1`t$HostName`t$tag"
  Set-Content -LiteralPath $hosts -Value $lines -Encoding ASCII -Force
  ipconfig /flushdns | Out-Null
}
function Assert-PortAvailable([int]$Port,[string]$Purpose){
  $listener=Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
  if($listener){throw "TCP port $Port for $Purpose is already in use by PID $($listener.OwningProcess). Choose an unused port or release the existing listener."}
}

Assert-Admin
if(-not [Environment]::Is64BitOperatingSystem){throw 'MAM Demo requires 64-bit Windows.'}
$os=[Environment]::OSVersion.Version
if($os.Major -lt 10 -or $os.Build -lt 22000){throw "MAM Demo requires Windows 11 (build 22000 or later). Current version: $os"}
if($ApiPort -eq $WebPort -or $ApiPort -lt 1 -or $ApiPort -gt 65535 -or $WebPort -lt 1 -or $WebPort -gt 65535){throw 'Demo API and Web ports must be distinct valid TCP ports.'}

$demoOrigin=if($WebPort -eq 80){"http://$DemoHost"}else{"http://$DemoHost`:$WebPort"}
$demoUrl="$demoOrigin/"

foreach($task in @('Diwan MAM Demo API','Diwan MAM Demo Web')){Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue}
Start-Sleep -Milliseconds 500
Assert-PortAvailable $WebPort 'MAM Demo Web'
Assert-PortAvailable $ApiPort 'MAM Demo API'

$configRoot=Join-Path $dataRoot 'config'
$dbRoot=Join-Path $dataRoot 'database'
$primaryRoot=Join-Path $dataRoot 'primary'
$backupRoot=Join-Path $dataRoot 'backup'
$ingestRoot=Join-Path $dataRoot 'ingest-cache'
New-Item -ItemType Directory -Force -Path $dataRoot,$configRoot,$dbRoot,$primaryRoot,$backupRoot,$ingestRoot,$logRoot | Out-Null

$template=Join-Path $InstallRoot 'config\appsettings.Demo.template.json'
if(-not(Test-Path -LiteralPath $template -PathType Leaf)){throw "Demo configuration template is missing: $template"}
$cfg=Get-Content -Raw -LiteralPath $template | ConvertFrom-Json
$cfg.Server.PublicBaseUrl="http://127.0.0.1:$ApiPort"
$cfg.Server.AllowedOrigins=@($demoOrigin)
$cfg.Database.SqlitePath=Join-Path $dbRoot 'mam-demo.db'
$cfg.Storage.Primary.Root=$primaryRoot
$cfg.Storage.Backup.Root=$backupRoot
$cfg.Desktop.IngestCache.Root=$ingestRoot
$configPath=Join-Path $configRoot 'appsettings.Demo.json'
$cfg | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $configPath -Encoding UTF8

$environmentDescriptorPath=Join-Path $InstallRoot 'web\wwwroot\client-environment.json'
[ordered]@{
  environmentName='Demo'
  apiBaseUrl="http://127.0.0.1:$ApiPort"
  webBaseUrl=$demoOrigin
  desktopInstallerPath='/downloads/DiwanMAM-Desktop-Setup-current-x64.exe'
} | ConvertTo-Json | Set-Content -LiteralPath $environmentDescriptorPath -Encoding UTF8

Set-DemoHostsEntry $DemoHost
Register-DemoTask 'Diwan MAM Demo API' 'Api' $configPath
Register-DemoTask 'Diwan MAM Demo Web' 'Web' $configPath

if($StartServices -eq 1){
  Start-ScheduledTask -TaskName 'Diwan MAM Demo API'
  $apiReady=$false
  for($i=0;$i -lt 40;$i++){
    try{$r=Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$ApiPort/health/ready" -TimeoutSec 2;if($r.StatusCode -eq 200){$apiReady=$true;break}}catch{}
    Start-Sleep -Milliseconds 500
  }
  if(-not $apiReady){throw "MAM Demo API did not become ready. Check $logRoot and the scheduled task history."}
  Start-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  $webReady=$false
  for($i=0;$i -lt 40;$i++){
    try{$r=Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$WebPort/version" -Headers @{Host=$DemoHost} -TimeoutSec 2;if($r.StatusCode -eq 200){$webReady=$true;break}}catch{}
    Start-Sleep -Milliseconds 500
  }
  if(-not $webReady){throw "MAM Demo Web did not become ready at $demoUrl"}
}

[ordered]@{
  status='configured'; environment='Demo'; database='SQLite'; databasePath=(Join-Path $dbRoot 'mam-demo.db');
  url=$demoUrl; api="http://127.0.0.1:$ApiPort/"; primary=$primaryRoot; backup=$backupRoot;
  offline=$true; windowsMinimumBuild=22000
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'setup-state.json') -Encoding UTF8

Write-Host "MAM Demo ready: $demoUrl" -ForegroundColor Green