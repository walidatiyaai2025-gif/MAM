param(
  [string]$OutputRoot = "",
  [string]$Version = "",
  [string]$Commit = ""
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($Version)) {
  [xml]$props = Get-Content -Raw -LiteralPath (Join-Path $repo 'Directory.Build.props')
  $Version = [string]$props.Project.PropertyGroup.Version
}
if ([string]::IsNullOrWhiteSpace($Commit)) {
  $Commit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { 'local' } else { $env:GITHUB_SHA }
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $repo 'artifacts\mac-uploader'
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$stage = Join-Path ([IO.Path]::GetTempPath()) ('mam-mac-uploader-' + [Guid]::NewGuid().ToString('N'))
$project = Join-Path $repo 'src\MAM.MacUploader\MAM.MacUploader.csproj'
$appRoot = Join-Path $stage 'Diwan MAM Uploader.app'
$contents = Join-Path $appRoot 'Contents'
$macOs = Join-Path $contents 'MacOS'
$resources = Join-Path $contents 'Resources'
$arm64 = Join-Path $resources 'arm64'
$x64 = Join-Path $resources 'x64'

function Publish-MacRuntime([string]$Rid, [string]$Destination) {
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  dotnet publish $project -c Release -r $Rid --self-contained true -p:PublishSingleFile=false -p:SourceRevisionId=$Commit -o $Destination
  if ($LASTEXITCODE -ne 0) { throw "Mac uploader publish failed for $Rid." }
  $exe = Join-Path $Destination 'MAM.MacUploader'
  if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published Mac executable missing for $Rid." }
}

function Add-ZipEntry(
  [System.IO.Compression.ZipArchive]$Zip,
  [string]$FilePath,
  [string]$EntryName,
  [bool]$Executable
) {
  $entry = $Zip.CreateEntry($EntryName.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal)
  $mode = if ($Executable) { 33261 } else { 33188 }
  $entry.ExternalAttributes = $mode -shl 16
  $input = [IO.File]::OpenRead($FilePath)
  try {
    $output = $entry.Open()
    try { $input.CopyTo($output) } finally { $output.Dispose() }
  } finally { $input.Dispose() }
}

function Set-ZipUnixMetadata([string]$ZipPath) {
  # ZipArchive created on Windows marks entries as DOS-origin even when POSIX mode
  # bits are present. Finder/Archive Utility can then discard executable bits.
  # Rewrite each central-directory entry as Unix-origin and stamp deterministic
  # 100755/100644 modes so both Mac runtimes launch after a normal Finder extract.
  $bytes = [IO.File]::ReadAllBytes($ZipPath)
  if ($bytes.Length -lt 22) { throw 'Mac uploader ZIP is too small to contain a valid central directory.' }

  $minimumEocd = [Math]::Max(0, $bytes.Length - 65557)
  $eocd = -1
  for ($i = $bytes.Length - 22; $i -ge $minimumEocd; $i--) {
    if (
      $bytes[$i] -eq 0x50 -and
      $bytes[$i + 1] -eq 0x4b -and
      $bytes[$i + 2] -eq 0x05 -and
      $bytes[$i + 3] -eq 0x06
    ) {
      $eocd = $i
      break
    }
  }
  if ($eocd -lt 0) { throw 'Mac uploader ZIP end-of-central-directory record was not found.' }

  $entryCount = [BitConverter]::ToUInt16($bytes, $eocd + 10)
  $centralOffset = [BitConverter]::ToUInt32($bytes, $eocd + 16)
  if ($entryCount -eq 0xffff -or $centralOffset -eq 0xffffffff) {
    throw 'ZIP64 Mac uploader packages are not supported by the deterministic Unix metadata normalizer.'
  }

  $cursor = [int]$centralOffset
  for ($entryIndex = 0; $entryIndex -lt $entryCount; $entryIndex++) {
    if ($cursor + 46 -gt $bytes.Length) { throw 'Mac uploader ZIP central directory is truncated.' }
    if (
      $bytes[$cursor] -ne 0x50 -or
      $bytes[$cursor + 1] -ne 0x4b -or
      $bytes[$cursor + 2] -ne 0x01 -or
      $bytes[$cursor + 3] -ne 0x02
    ) {
      throw "Invalid central-directory signature at entry $entryIndex."
    }

    $nameLength = [BitConverter]::ToUInt16($bytes, $cursor + 28)
    $extraLength = [BitConverter]::ToUInt16($bytes, $cursor + 30)
    $commentLength = [BitConverter]::ToUInt16($bytes, $cursor + 32)
    $nextCursor = $cursor + 46 + $nameLength + $extraLength + $commentLength
    if ($nextCursor -gt $bytes.Length) { throw 'Mac uploader ZIP central-directory entry is truncated.' }

    $entryName = [Text.Encoding]::UTF8.GetString($bytes, $cursor + 46, $nameLength)
    $isExecutable = (
      $entryName -eq 'Diwan MAM Uploader.app/Contents/MacOS/DiwanMAMUploader' -or
      $entryName -eq 'Diwan MAM Uploader.app/Contents/Resources/arm64/MAM.MacUploader' -or
      $entryName -eq 'Diwan MAM Uploader.app/Contents/Resources/x64/MAM.MacUploader'
    )

    # ZIP "version made by" high byte: 3 = Unix.
    $bytes[$cursor + 5] = 3

    # POSIX regular-file modes: 0100755 for launchers, 0100644 for data files.
    [uint32]$mode = if ($isExecutable) { 33261 } else { 33188 }
    [uint32]$externalAttributes = $mode * 65536
    $attributeBytes = [BitConverter]::GetBytes($externalAttributes)
    [Array]::Copy($attributeBytes, 0, $bytes, $cursor + 38, 4)

    $cursor = $nextCursor
  }

  [IO.File]::WriteAllBytes($ZipPath, $bytes)
}

try {
  New-Item -ItemType Directory -Force -Path $macOs,$arm64,$x64 | Out-Null
  Publish-MacRuntime 'osx-arm64' $arm64
  Publish-MacRuntime 'osx-x64' $x64

  $launcher = @'
#!/bin/sh
set -eu
BASE="$(CDPATH= cd -- "$(dirname -- "$0")/../Resources" && pwd)"
ARCH="$(uname -m)"
case "$ARCH" in
  arm64|aarch64) RUNTIME="arm64" ;;
  x86_64|amd64) RUNTIME="x64" ;;
  *) echo "Unsupported Mac architecture: $ARCH" >&2; exit 64 ;;
esac
exec "$BASE/$RUNTIME/MAM.MacUploader" "$@"
'@
  $launcherPath = Join-Path $macOs 'DiwanMAMUploader'
  [IO.File]::WriteAllText($launcherPath, ($launcher.Replace("`r`n","`n").Replace("`r","`n")), (New-Object Text.UTF8Encoding($false)))

  $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>Diwan MAM Uploader</string>
  <key>CFBundleExecutable</key><string>DiwanMAMUploader</string>
  <key>CFBundleIdentifier</key><string>kw.gov.da.mam.uploader</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Diwan MAM Uploader</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$Version</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
  $plistPath = Join-Path $contents 'Info.plist'
  [IO.File]::WriteAllText($plistPath, ($plist.Replace("`r`n","`n").Replace("`r","`n")), (New-Object Text.UTF8Encoding($false)))

  Add-Type -AssemblyName System.IO.Compression
  Add-Type -AssemblyName System.IO.Compression.FileSystem

  $zipName = "DiwanMAM-Mac-Uploader-$Version-universal.zip"
  $zipPath = Join-Path $OutputRoot $zipName
  Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

  $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
  try {
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
      $rootParent = [IO.Path]::GetFullPath((Split-Path -Parent $appRoot)).TrimEnd('\','/')
      $relativePrefixLength = $rootParent.Length + 1
      foreach ($file in Get-ChildItem -LiteralPath $appRoot -File -Recurse | Sort-Object FullName) {
        $fullPath = [IO.Path]::GetFullPath($file.FullName)
        if (-not $fullPath.StartsWith($rootParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
          throw "Mac package file escaped the staging root: $fullPath"
        }
        $relative = $fullPath.Substring($relativePrefixLength)
        $isExecutable = $relative -like '*\Contents\MacOS\DiwanMAMUploader' -or $file.Name -eq 'MAM.MacUploader'
        Add-ZipEntry $zip $file.FullName $relative $isExecutable
      }
    } finally { $zip.Dispose() }
  } finally { $stream.Dispose() }

  Set-ZipUnixMetadata $zipPath

  $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $manifest = [ordered]@{
    version = $Version
    commit = $Commit
    package = $zipName
    sha256 = $hash
    architectures = @('arm64','x64')
    minimumMacOS = '12.0'
    productionOrigin = 'https://mam.da.gov.kw/'
    scope = 'upload-only'
  }
  $manifestPath = Join-Path $OutputRoot 'mac-uploader-manifest.json'
  $manifestJson = $manifest | ConvertTo-Json -Depth 5
  [IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object Text.UTF8Encoding($false)))

  Write-Host "Mac uploader package: $zipPath"
  Write-Host "SHA256: $hash"
}
finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
