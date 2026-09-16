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
try {
  switch($Component) {
    'Api' {
      $env:ASPNETCORE_URLS="http://127.0.0.1:$ApiPort"
      & (Join-Path $InstallRoot 'api\MAM.Api.exe')
      exit $LASTEXITCODE
    }
    'Web' {
      $env:MAM_API_BASE_URL="http://127.0.0.1:$ApiPort/"
      $env:ASPNETCORE_URLS="http://127.0.0.1:$WebPort"
      & (Join-Path $InstallRoot 'web\MAM.Web.exe')
      exit $LASTEXITCODE
    }
  }
}
finally {
  $env:MAM_CONFIG_PATH=$null
  $env:MAM_AUTH_MODE=$null
  $env:MAM_DEV_USER=$null
  $env:MAM_API_BASE_URL=$null
}
