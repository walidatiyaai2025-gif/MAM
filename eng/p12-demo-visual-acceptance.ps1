param(
  [Parameter(Mandatory = $true)]
  [string] $SetupRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SetupRoot = [IO.Path]::GetFullPath($SetupRoot)
$work = Join-Path $env:RUNNER_TEMP ('mam-demo-visual-' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $work 'Install'
$setupLog = Join-Path $work 'demo-visual-setup.log'
$dataRoot = Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Demo'
$dbPath = Join-Path $dataRoot 'database\mam-demo.db'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$originalHosts = if (Test-Path -LiteralPath $hostsPath) { Get-Content -Raw -LiteralPath $hostsPath } else { '' }
$demoHost = 'demomam.da.gov.kw'
$apiPort = 15109
$webPort = 18090
$phase = 'initialization'

New-Item -ItemType Directory -Force -Path $work | Out-Null

function Set-Phase([string]$Name) {
  $script:phase = $Name
  Write-Host "========== P12 DEMO VISUAL PHASE: $Name =========="
}

function Assert-True([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw "[$script:phase] $Message" }
}

function Wait-Url([string]$Uri,[hashtable]$Headers=@{},[int]$Attempts=60) {
  for ($i=0; $i -lt $Attempts; $i++) {
    try {
      $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Headers $Headers -TimeoutSec 2
      if ($response.StatusCode -eq 200) { return $response }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  throw "[$script:phase] URL did not become ready: $Uri"
}

function Invoke-Api([string]$Method,[string]$Path,[object]$Body=$null) {
  $parameters = @{
    Method = $Method
    Uri = "http://127.0.0.1:$apiPort$Path"
    Headers = @{ 'X-MAM-Dev-User'='admin'; 'X-MAM-Client'='P12DemoVisualAcceptance' }
    UseBasicParsing = $true
    TimeoutSec = 20
  }
  if ($null -ne $Body) {
    $parameters.ContentType = 'application/json'
    $parameters.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
  }
  Invoke-WebRequest @parameters
}

function New-TestBmp([string]$Path) {
  $width = 16
  $height = 16
  $rowSize = [int]([Math]::Ceiling(($width * 3) / 4.0) * 4)
  $pixelBytes = $rowSize * $height
  $stream = [IO.File]::Open($Path,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {
    $writer = [IO.BinaryWriter]::new($stream)
    try {
      $writer.Write([byte]0x42); $writer.Write([byte]0x4D)
      $writer.Write([int](54 + $pixelBytes)); $writer.Write([int]0); $writer.Write([int]54)
      $writer.Write([int]40); $writer.Write([int]$width); $writer.Write([int]$height)
      $writer.Write([int16]1); $writer.Write([int16]24); $writer.Write([int]0); $writer.Write([int]$pixelBytes)
      $writer.Write([int]2835); $writer.Write([int]2835); $writer.Write([int]0); $writer.Write([int]0)
      for ($y=0; $y -lt $height; $y++) {
        for ($x=0; $x -lt $width; $x++) {
          $writer.Write([byte](($x * 13 + $y * 3) % 256))
          $writer.Write([byte](($x * 5 + $y * 17) % 256))
          $writer.Write([byte](($x * 19 + $y * 7) % 256))
        }
        for ($p=$width*3; $p -lt $rowSize; $p++) { $writer.Write([byte]0) }
      }
    } finally { $writer.Dispose() }
  } finally { $stream.Dispose() }
}

function Upload-DemoImage([string]$Path) {
  $length = (Get-Item -LiteralPath $Path).Length
  $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
  $created = (Invoke-Api 'POST' '/api/v1/uploads/sessions' @{
    title='Offline Demo Visual Acceptance Image'
    originalFileName='demo-visual.bmp'
    expectedLength=$length
    expectedSha256=$sha
  }).Content | ConvertFrom-Json
  $sessionId = [string]$created.session.sessionId
  Assert-True (-not [string]::IsNullOrWhiteSpace($sessionId)) 'Demo image upload session did not return an ID.'
  $put = Invoke-WebRequest -UseBasicParsing -Method Put -Uri "http://127.0.0.1:$apiPort/api/v1/uploads/sessions/$sessionId/chunks?offset=0" `
    -Headers @{ 'X-MAM-Dev-User'='admin'; 'X-MAM-Client'='P12DemoVisualAcceptance'; 'X-Chunk-SHA256'=$sha } `
    -ContentType 'application/octet-stream' -InFile $Path -TimeoutSec 20
  Assert-True ($put.StatusCode -eq 200) 'Demo image chunk upload failed.'
  $final = (Invoke-Api 'POST' "/api/v1/uploads/sessions/$sessionId/finalize").Content | ConvertFrom-Json
  Assert-True (-not [string]::IsNullOrWhiteSpace([string]$final.assetId)) 'Demo image finalize did not return an asset ID.'
  return [string]$final.assetId
}

function Search-Api([string]$Path) {
  $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "http://127.0.0.1:$apiPort/api/v1/discovery/image-search?limit=10" `
    -Headers @{ 'X-MAM-Dev-User'='admin'; 'X-MAM-Client'='P12DemoVisualAcceptance' } -ContentType 'image/bmp' -InFile $Path -TimeoutSec 20
  return ($response.Content | ConvertFrom-Json)
}

function Search-Web([string]$Path) {
  $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "http://127.0.0.1:$webPort/client-api/discovery/image-search?limit=10" `
    -Headers @{ Host=$demoHost } -ContentType 'image/bmp' -InFile $Path -TimeoutSec 20
  return ($response.Content | ConvertFrom-Json)
}

try {
  Set-Phase 'discover-artifact'
  $candidates = @(Get-ChildItem -LiteralPath $SetupRoot -Filter 'DiwanMAM-Demo-Setup-*-x64.exe')
  Assert-True ($candidates.Count -eq 1) "Expected exactly one Demo Setup EXE; found $($candidates.Count)."
  $demo = $candidates[0]

  Set-Phase 'clean-host'
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue

  Set-Phase 'install'
  $process = Start-Process -FilePath $demo.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG={0}" -f $setupLog),("/DIR={0}" -f $installRoot),("/APIPORT={0}" -f $apiPort),("/WEBPORT={0}" -f $webPort)) -Wait -PassThru
  Assert-True ($process.ExitCode -eq 0) "Demo visual installer failed with exit $($process.ExitCode)."
  Wait-Url -Uri "http://127.0.0.1:$apiPort/health/ready" | Out-Null
  Wait-Url -Uri "http://127.0.0.1:$webPort/version" -Headers @{ Host=$demoHost } | Out-Null
  Assert-True (Test-Path -LiteralPath $dbPath) 'Demo SQLite database was not created.'

  Set-Phase 'visual-health'
  $health = (Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$apiPort/health/visual-search" -TimeoutSec 20).Content | ConvertFrom-Json
  Assert-True ($health.status -eq 'Ready') 'Installed Demo visual-search health is not Ready.'
  Assert-True ($health.visual.isReady -eq $true) 'Installed Demo local visual provider is not ready.'
  Assert-True ($health.visual.dimensions -eq 768) 'Installed Demo visual vector dimensions changed unexpectedly.'

  Set-Phase 'upload-and-search'
  $imagePath = Join-Path $work 'demo-visual.bmp'
  New-TestBmp -Path $imagePath
  $assetId = Upload-DemoImage -Path $imagePath
  $apiResults = Search-Api -Path $imagePath
  $apiHit = @($apiResults.items | Where-Object { [string]$_.assetId -eq $assetId } | Select-Object -First 1)
  Assert-True ($apiHit.Count -eq 1) 'Installed Demo image search did not return its indexed image asset.'
  Assert-True ([double]$apiHit[0].score -gt 0.99) 'Installed Demo exact-image similarity score is unexpectedly low.'

  Set-Phase 'derived-thumbnail'
  $thumbPath = Join-Path $work 'asset-thumb.jpg'
  $thumbResponse = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$apiPort/api/v1/discovery/assets/$assetId/visual-thumbnail" `
    -Headers @{ 'X-MAM-Dev-User'='admin'; 'X-MAM-Client'='P12DemoVisualAcceptance' } -OutFile $thumbPath -PassThru -TimeoutSec 20
  Assert-True ($thumbResponse.StatusCode -eq 200) 'Installed Demo derived visual thumbnail endpoint failed.'
  Assert-True ($thumbResponse.Headers.'Content-Type' -like 'image/jpeg*') 'Installed Demo derived visual thumbnail is not JPEG.'
  Assert-True ((Get-Item -LiteralPath $thumbPath).Length -gt 0) 'Installed Demo derived visual thumbnail is empty.'

  Set-Phase 'web-proxy-search'
  $webResults = Search-Web -Path $imagePath
  Assert-True (@($webResults.items | Where-Object { [string]$_.assetId -eq $assetId }).Count -ge 1) 'Installed Demo Web proxy did not return the visual-search asset.'

  Set-Phase 'restart-persistence'
  Stop-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  Stop-ScheduledTask -TaskName 'Diwan MAM Demo API'
  Start-Sleep -Seconds 1
  Start-ScheduledTask -TaskName 'Diwan MAM Demo API'
  Wait-Url -Uri "http://127.0.0.1:$apiPort/health/ready" | Out-Null
  Start-ScheduledTask -TaskName 'Diwan MAM Demo Web'
  Wait-Url -Uri "http://127.0.0.1:$webPort/version" -Headers @{ Host=$demoHost } | Out-Null
  $afterRestart = Search-Api -Path $imagePath
  Assert-True (@($afterRestart.items | Where-Object { [string]$_.assetId -eq $assetId }).Count -ge 1) 'Demo visual index did not persist across API/Web restart.'

  Set-Phase 'uninstall-preservation'
  $uninstall = Start-Process -FilePath (Join-Path $installRoot 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
  Assert-True ($uninstall.ExitCode -eq 0) 'Demo visual uninstall failed.'
  Assert-True (Test-Path -LiteralPath $dbPath) 'Demo visual data was not preserved by uninstall.'

  Set-Phase 'success'
  Write-Host 'P12 offline Demo visual runtime acceptance: SUCCESS'
}
catch {
  Write-Host "P12 offline Demo visual runtime acceptance FAILED in phase [$phase]"
  Write-Host ($_ | Out-String)
  if (Test-Path -LiteralPath $setupLog) { Get-Content -LiteralPath $setupLog -Tail 250 | ForEach-Object { Write-Host $_ } }
  $runtimeLogRoot = Join-Path $dataRoot 'logs'
  if (Test-Path -LiteralPath $runtimeLogRoot) {
    Get-ChildItem -LiteralPath $runtimeLogRoot -File -ErrorAction SilentlyContinue | ForEach-Object {
      Write-Host "----- $($_.Name) -----"
      Get-Content -LiteralPath $_.FullName -Tail 250 | ForEach-Object { Write-Host $_ }
    }
  }
  throw
}
finally {
  foreach ($taskName in @('Diwan MAM Demo API','Diwan MAM Demo Web')) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  }
  try { Set-Content -LiteralPath $hostsPath -Value $originalHosts -NoNewline -Encoding ASCII -Force; ipconfig /flushdns | Out-Null } catch { Write-Host "Hosts restore warning: $($_.Exception.Message)" }
  Remove-Item -LiteralPath $dataRoot,$work -Recurse -Force -ErrorAction SilentlyContinue
}
