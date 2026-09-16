param(
  [Parameter(Mandatory=$true)][ValidateSet('Api','Web')][string]$Component,
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$ConfigPath,
  [int]$ApiPort = 5099,
  [int]$WebPort = 80
)
$ErrorActionPreference='Stop'
$env:MAM_CONFIG_PATH=$ConfigPath
$env:MAM_AUTH_MODE='Local'
$env:MAM_DEV_USER='admin'
$env:DOTNET_ENVIRONMENT='Demo'
$env:ASPNETCORE_ENVIRONMENT='Demo'

$logRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Demo\logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$logPath=Join-Path $logRoot (([string]$Component).ToLowerInvariant() + '-runtime.log')
Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ("[{0}] Starting {1}; InstallRoot={2}; ConfigPath={3}; ApiPort={4}; WebPort={5}" -f [DateTimeOffset]::UtcNow.ToString('O'),$Component,$InstallRoot,$ConfigPath,$ApiPort,$WebPort)

try {
  switch($Component) {
    'Api' {
      $env:ASPNETCORE_URLS="http://127.0.0.1:$ApiPort"
      & (Join-Path $InstallRoot 'api\MAM.Api.exe') *>> $logPath
      $exitCode=$LASTEXITCODE
      Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ("[{0}] API exited with code {1}." -f [DateTimeOffset]::UtcNow.ToString('O'),$exitCode)
      exit $exitCode
    }
    'Web' {
      $env:MAM_API_BASE_URL="http://127.0.0.1:$ApiPort/"
      $env:ASPNETCORE_URLS="http://127.0.0.1:$WebPort"
      & (Join-Path $InstallRoot 'web\MAM.Web.exe') *>> $logPath
      $exitCode=$LASTEXITCODE
      Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ("[{0}] Web exited with code {1}." -f [DateTimeOffset]::UtcNow.ToString('O'),$exitCode)
      exit $exitCode
    }
  }
}
catch {
  Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ("[{0}] PowerShell launcher failure: {1}" -f [DateTimeOffset]::UtcNow.ToString('O'),($_ | Out-String))
  throw
}
finally {
  $env:MAM_CONFIG_PATH=$null
  $env:MAM_AUTH_MODE=$null
  $env:MAM_DEV_USER=$null
  $env:MAM_API_BASE_URL=$null
}