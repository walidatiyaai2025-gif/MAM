param([string]$OutputRoot = "")
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -Raw -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw "Unexpected version '$version'." }
$numericVersion = "$($matches[1]).$($matches[2]).$($matches[3]).0"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'artifacts\setups' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('mam-setup-' + [Guid]::NewGuid().ToString('N'))
$brand = Join-Path $stage 'brand'
New-Item -ItemType Directory -Force -Path $OutputRoot,$stage,$brand | Out-Null

function Publish([string]$Project,[string]$Destination) {
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  dotnet publish (Join-Path $repo $Project) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:SourceRevisionId=$env:GITHUB_SHA -p:MamBuildNumber=$env:GITHUB_RUN_NUMBER -o $Destination
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project" }
}

function New-BrandBitmap([string]$Source,[string]$Destination,[int]$Width,[int]$Height,[int]$Padding) {
  Add-Type -AssemblyName System.Drawing
  $sourceImage = [Drawing.Image]::FromFile($Source)
  try {
    $bitmap = New-Object Drawing.Bitmap $Width,$Height
    try {
      $graphics = [Drawing.Graphics]::FromImage($bitmap)
      try {
        $graphics.Clear([Drawing.Color]::FromArgb(7,24,46))
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $availableW=$Width-(2*$Padding); $availableH=$Height-(2*$Padding)
        $scale=[Math]::Min($availableW/$sourceImage.Width,$availableH/$sourceImage.Height)
        $w=[int]($sourceImage.Width*$scale); $h=[int]($sourceImage.Height*$scale)
        $x=[int](($Width-$w)/2); $y=[int](($Height-$h)/2)
        $graphics.DrawImage($sourceImage,$x,$y,$w,$h)
        $gold = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(181,138,42)),2
        try { $graphics.DrawLine($gold,$Padding,$Height-$Padding-2,$Width-$Padding,$Height-$Padding-2) } finally { $gold.Dispose() }
      } finally { $graphics.Dispose() }
      $bitmap.Save($Destination,[Drawing.Imaging.ImageFormat]::Bmp)
    } finally { $bitmap.Dispose() }
  } finally { $sourceImage.Dispose() }
}

function New-SetupIcon([string]$Source,[string]$Destination) {
  Add-Type -AssemblyName System.Drawing
  $sourceImage=[Drawing.Image]::FromFile($Source)
  $pngTemp=[IO.Path]::GetTempFileName()
  try {
    $bitmap=New-Object Drawing.Bitmap 64,64
    try {
      $graphics=[Drawing.Graphics]::FromImage($bitmap)
      try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scale=[Math]::Min(60/$sourceImage.Width,60/$sourceImage.Height)
        $w=[int]($sourceImage.Width*$scale); $h=[int]($sourceImage.Height*$scale)
        $graphics.DrawImage($sourceImage,[int]((64-$w)/2),[int]((64-$h)/2),$w,$h)
      } finally { $graphics.Dispose() }
      $bitmap.Save($pngTemp,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    $bytes=[IO.File]::ReadAllBytes($pngTemp)
    $stream=New-Object IO.MemoryStream
    $writer=New-Object IO.BinaryWriter $stream
    try {
      $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
      $writer.Write([Byte]64); $writer.Write([Byte]64); $writer.Write([Byte]0); $writer.Write([Byte]0)
      $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]22)
      $writer.Write($bytes); $writer.Flush(); [IO.File]::WriteAllBytes($Destination,$stream.ToArray())
    } finally { $writer.Dispose(); $stream.Dispose() }
  } finally { $sourceImage.Dispose(); Remove-Item $pngTemp -Force -ErrorAction SilentlyContinue }
}

try {
  Publish 'src/MAM.Desktop/MAM.Desktop.csproj' (Join-Path $stage 'desktop')
  Publish 'src/MAM.Api/MAM.Api.csproj' (Join-Path $stage 'server\api')
  Publish 'src/MAM.Web/MAM.Web.csproj' (Join-Path $stage 'server\web')
  Publish 'src/MAM.Worker/MAM.Worker.csproj' (Join-Path $stage 'server\worker')
  Publish 'tools/MAM.Deployment/MAM.Deployment.csproj' (Join-Path $stage 'server\sql\tool')
  Publish 'tools/MAM.BrandExport/MAM.BrandExport.csproj' (Join-Path $stage 'brand-tool')

  New-Item -ItemType Directory -Force -Path (Join-Path $stage 'server\sql\migrations'),(Join-Path $stage 'server\config'),(Join-Path $stage 'server\setup') | Out-Null
  Copy-Item (Join-Path $repo 'database\migrations\*.sql') (Join-Path $stage 'server\sql\migrations') -Force
  Copy-Item (Join-Path $repo 'config\appsettings.Production.template.json') (Join-Path $stage 'server\config') -Force
  Copy-Item (Join-Path $repo 'deploy\setup\Configure-MamServer.ps1') (Join-Path $stage 'server\setup') -Force
  Copy-Item (Join-Path $repo 'deploy\setup\Start-MamComponent.ps1') (Join-Path $stage 'server\setup') -Force
  Copy-Item (Join-Path $repo 'deploy\setup\Uninstall-MamServer.ps1') (Join-Path $stage 'server\setup') -Force

  & (Join-Path $stage 'brand-tool\MAM.BrandExport.exe') $brand
  if ($LASTEXITCODE -ne 0) { throw 'Brand export failed.' }
  New-BrandBitmap (Join-Path $brand 'diwan-al-amiri-crest.png') (Join-Path $brand 'wizard-large.bmp') 164 314 14
  New-BrandBitmap (Join-Path $brand 'diwan-al-amiri-crest.png') (Join-Path $brand 'wizard-small.bmp') 55 58 4
  New-SetupIcon (Join-Path $brand 'diwan-al-amiri-crest.png') (Join-Path $brand 'diwan-setup.ico')

  $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
  if (-not (Test-Path -LiteralPath $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
  foreach($script in @('desktop.iss','server.iss')) {
    & $iscc "/DMyVersion=$version" "/DNumericVersion=$numericVersion" "/DSourceRoot=$stage" "/DBrandRoot=$brand" "/DOutputDir=$OutputRoot" (Join-Path $repo "deploy\setup\$script")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed for $script" }
  }

  $files=Get-ChildItem -LiteralPath $OutputRoot -Filter 'DiwanMAM-*-Setup-*.exe' | Sort-Object Name
  if ($files.Count -ne 2) { throw "Expected exactly two Setup EXEs; found $($files.Count)." }
  $manifest=[ordered]@{
    version=$version; commit=$env:GITHUB_SHA; build=$env:GITHUB_RUN_NUMBER; brandingSha256='bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb';
    artifacts=@($files | ForEach-Object { [ordered]@{ file=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant() } })
  }
  $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputRoot 'setup-manifest.json') -Encoding UTF8
  Copy-Item (Join-Path $brand 'brand-manifest.json') (Join-Path $OutputRoot 'brand-manifest.json') -Force
  Write-Host "Built premium Diwan MAM Desktop + Server setups for $version"
}
finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
