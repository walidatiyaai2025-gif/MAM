param(
  [Parameter(Mandatory=$true)][ValidateSet('Api','Web','Worker')][string]$Component,
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][string]$ConfigPath,
  [Parameter(Mandatory=$true)][string]$SqlSecretPath,
  [Parameter(Mandatory=$true)][string]$PublicHost,
  [Parameter(Mandatory=$true)][int]$ApiPort,
  [Parameter(Mandatory=$true)][int]$WebPort,
  [Parameter(Mandatory=$true)][ValidateSet('Production','UAT')][string]$EnvironmentName,
  [string]$TlsPfxPath = '',
  [string]$TlsSecretPath = ''
)
$ErrorActionPreference = 'Stop'

function Unprotect-Secret([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Protected secret not found: $Path" }
  $protected = [IO.File]::ReadAllBytes($Path)
  $plain = [Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
  try { return [Text.Encoding]::UTF8.GetString($plain) }
  finally { [Array]::Clear($plain,0,$plain.Length) }
}

$sql = Unprotect-Secret $SqlSecretPath
try {
  $env:MAM_CONFIG_PATH = $ConfigPath
  $env:MAM_SQL_CONNECTION_STRING = $sql
  $env:DOTNET_ENVIRONMENT = $EnvironmentName
  $env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
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
      $env:MAM_API_BASE_URL = "${scheme}://${PublicHost}:$ApiPort/"
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
  $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $null
  $sql = $null
}
