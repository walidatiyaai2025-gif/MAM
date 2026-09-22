param(
  [Parameter(Mandatory=$true)][ValidateSet('Api','Web','Worker')][string]$Component,
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$ConfigPath,
  [Parameter(Mandatory=$true)][string]$SqlSecretPath,
  [string]$InternalAuthSecretPath = '',
  [Parameter(Mandatory=$true)][string]$PublicHost,
  [Parameter(Mandatory=$true)][int]$ApiPort,
  [Parameter(Mandatory=$true)][int]$WebPort,
  [Parameter(Mandatory=$true)][ValidateSet('Production','UAT')][string]$EnvironmentName,
  [string]$TlsPfxPath = '',
  [string]$TlsSecretPath = ''
)
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not guarantee that System.Security is loaded before
# ProtectedData is first referenced. Load it explicitly so startup-task DPAPI
# secret unprotect is deterministic on clean Windows Server hosts.
Add-Type -AssemblyName System.Security -ErrorAction Stop

function Unprotect-Secret([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Protected secret not found: $Path" }
  $protected = [IO.File]::ReadAllBytes($Path)
  $plain = [Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
  try { return [Text.Encoding]::UTF8.GetString($plain) }
  finally { [Array]::Clear($plain,0,$plain.Length) }
}

$sql = Unprotect-Secret $SqlSecretPath
$internalAuthKey = $null
try {
  $env:MAM_CONFIG_PATH = $ConfigPath
  $configRoot = Split-Path -Parent $ConfigPath
  $dataRoot = Split-Path -Parent $configRoot
  $runtimeLogRoot = Join-Path (Join-Path $dataRoot 'logs') 'runtime-inspector'
  New-Item -ItemType Directory -Force -Path $runtimeLogRoot | Out-Null
  $env:MAM_RUNTIME_LOG_PATH = $runtimeLogRoot
  $env:MAM_SQL_CONNECTION_STRING = $sql
  $env:DOTNET_ENVIRONMENT = $EnvironmentName
  $env:ASPNETCORE_ENVIRONMENT = $EnvironmentName

  $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
  $authMode = [string]$config.Auth.Mode
  $env:MAM_AUTH_MODE = $authMode

  if (-not [string]::IsNullOrWhiteSpace($InternalAuthSecretPath)) {
    $internalAuthKey = Unprotect-Secret $InternalAuthSecretPath
    $env:MAM_INTERNAL_AUTH_KEY = $internalAuthKey
  }
  elseif ($authMode -eq 'ActiveDirectory') {
    throw 'ActiveDirectory mode requires the protected internal authentication secret.'
  }

  $scheme = if ($EnvironmentName -eq 'Production') { 'https' } else { 'http' }

  if ($EnvironmentName -eq 'Production') {
    if ([string]::IsNullOrWhiteSpace($TlsPfxPath) -or [string]::IsNullOrWhiteSpace($TlsSecretPath)) { throw 'Production TLS material is missing.' }
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $TlsPfxPath
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password = Unprotect-Secret $TlsSecretPath
  }

  switch ($Component) {
    'Api' {
      $env:ASPNETCORE_URLS = "${scheme}://0.0.0.0:$ApiPort"
      & (Join-Path $InstallRoot 'api\MAM.Api.exe')
      exit $LASTEXITCODE
    }
    'Web' {
      # Preserve the public API URI for TLS SNI/certificate validation and signed request identity,
      # but force the TCP socket to the local API listener. This removes DNS/public-hairpin
      # dependency when Web and API run on the same Production server.
      $env:MAM_API_BASE_URL = "${scheme}://${PublicHost}:$ApiPort/"
      $env:MAM_API_CONNECT_LOOPBACK = '1'
      $env:ASPNETCORE_URLS = "${scheme}://0.0.0.0:$WebPort"
      & (Join-Path $InstallRoot 'web\MAM.Web.exe')
      exit $LASTEXITCODE
    }
    'Worker' {
      & (Join-Path $InstallRoot 'worker\MAM.Worker.exe')
      exit $LASTEXITCODE
    }
  }
}
finally {
  $env:MAM_SQL_CONNECTION_STRING = $null
  $env:MAM_INTERNAL_AUTH_KEY = $null
  $env:MAM_AUTH_MODE = $null
  $env:MAM_API_CONNECT_LOOPBACK = $null
  $env:MAM_RUNTIME_LOG_PATH = $null
  $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $null
  $sql = $null
  $internalAuthKey = $null
}
