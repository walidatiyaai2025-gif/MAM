[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CaptureFile,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [Parameter(Mandatory=$true)][string]$WorkstationId,
    [Parameter(Mandatory=$true)][string]$Provider,
    [Parameter(Mandatory=$true)][string]$DeviceId,
    [Parameter(Mandatory=$true)][string]$TapeId,
    [Parameter(Mandatory=$true)][string]$VideoProfile,
    [Parameter(Mandatory=$true)][string]$AudioProfile,
    [Parameter(Mandatory=$true)][string]$TimecodeSource,
    [Parameter(Mandatory=$true)][long]$CaptureDurationSeconds,
    [Parameter(Mandatory=$true)][long]$DroppedFrames,
    [Parameter(Mandatory=$true)][long]$ApprovedDroppedFrameThreshold,
    [Parameter(Mandatory=$true)][long]$PrimaryLength,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$PrimarySha256,
    [Parameter(Mandatory=$true)][long]$BackupLength,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$BackupSha256,
    [Parameter(Mandatory=$true)][string]$DriverVersion,
    [Parameter(Mandatory=$true)][string]$CaptureCardModel,
    [Parameter(Mandatory=$true)][string]$TapeDeckModel,
    [Parameter(Mandatory=$true)][string]$InputName,
    [Parameter(Mandatory=$true)][string]$Container,
    [Parameter(Mandatory=$true)][string]$Codec,
    [Parameter(Mandatory=$true)][string]$OperatorName,
    [switch]$DeviceDiscoveryVerified,
    [switch]$UnsupportedProfileFailClosedVerified,
    [switch]$PreflightVerified,
    [switch]$PreviewVerified,
    [switch]$PreviewUnavailableAccepted,
    [switch]$AudioMetersVerified,
    [switch]$AudioMetersUnavailableAccepted,
    [switch]$TimecodeVerified,
    [switch]$TimecodeUnavailableAccepted,
    [switch]$RecordFinalizeVerified,
    [switch]$CaptureMetadataVerified,
    [switch]$CentralApiUploadVerified,
    [switch]$CatalogVisibilityVerified,
    [switch]$BackupProtectedVerified,
    [switch]$RestartRecoveryVerified,
    [switch]$NetworkRecoveryVerified,
    [switch]$CacheCleanupVerified,
    [switch]$RtlLtrUxVerified,
    [switch]$SecurityBoundaryVerified,
    [switch]$OwnerSiteAccepted,
    [string]$NormalizedTimecode = '',
    [string]$OwnerSiteAcceptanceReference = ''
)

$ErrorActionPreference = 'Stop'

function Add-Failure([System.Collections.Generic.List[string]]$List, [string]$Message) {
    $List.Add($Message) | Out-Null
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'P07 real-hardware evidence collection must run on Windows.'
}

if ($CaptureDurationSeconds -le 0) {
    throw 'CaptureDurationSeconds must be positive. The approved representative duration is an owner/site decision.'
}
if ($DroppedFrames -lt 0) {
    throw 'DroppedFrames cannot be negative.'
}
if ($ApprovedDroppedFrameThreshold -lt 0) {
    throw 'ApprovedDroppedFrameThreshold must be explicitly supplied and cannot be negative.'
}
if ($PrimaryLength -le 0 -or $BackupLength -le 0) {
    throw 'PrimaryLength and BackupLength must both be positive.'
}

$resolvedCapture = (Resolve-Path -LiteralPath $CaptureFile).Path
$captureItem = Get-Item -LiteralPath $resolvedCapture
if ($captureItem.PSIsContainer -or $captureItem.Length -le 0) {
    throw 'CaptureFile must be a non-empty finalized capture artifact.'
}

New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$resolvedEvidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path

$captureHash = (Get-FileHash -LiteralPath $resolvedCapture -Algorithm SHA256).Hash.ToLowerInvariant()
$primaryHash = $PrimarySha256.ToLowerInvariant()
$backupHash = $BackupSha256.ToLowerInvariant()
$failures = New-Object 'System.Collections.Generic.List[string]'

if ($DroppedFrames -gt $ApprovedDroppedFrameThreshold) {
    Add-Failure $failures "Dropped frames ($DroppedFrames) exceed approved threshold ($ApprovedDroppedFrameThreshold)."
}
if (-not $DeviceDiscoveryVerified) {
    Add-Failure $failures 'Approved real device discovery/selection has not been verified.'
}
if (-not $UnsupportedProfileFailClosedVerified) {
    Add-Failure $failures 'Unsupported/degraded device or profile fail-closed behavior has not been verified.'
}
if (-not $PreflightVerified) {
    Add-Failure $failures 'Physical-path preflight has not been verified.'
}
if (-not ($PreviewVerified -or $PreviewUnavailableAccepted)) {
    Add-Failure $failures 'Live preview is neither verified nor explicitly accepted as unavailable/error behavior.'
}
if (-not ($AudioMetersVerified -or $AudioMetersUnavailableAccepted)) {
    Add-Failure $failures 'Audio meters are neither verified nor explicitly accepted as unavailable/error behavior.'
}
if (-not ($TimecodeVerified -or $TimecodeUnavailableAccepted)) {
    Add-Failure $failures 'Timecode is neither verified nor explicitly accepted as unavailable/error behavior.'
}
if ($PreviewVerified -and $PreviewUnavailableAccepted) {
    Add-Failure $failures 'Preview evidence is contradictory: both Verified and UnavailableAccepted were supplied.'
}
if ($AudioMetersVerified -and $AudioMetersUnavailableAccepted) {
    Add-Failure $failures 'Audio-meter evidence is contradictory: both Verified and UnavailableAccepted were supplied.'
}
if ($TimecodeVerified -and $TimecodeUnavailableAccepted) {
    Add-Failure $failures 'Timecode evidence is contradictory: both Verified and UnavailableAccepted were supplied.'
}
if (-not $RecordFinalizeVerified) {
    Add-Failure $failures 'Real record/stop/finalize behavior has not been verified.'
}
if (-not $CaptureMetadataVerified) {
    Add-Failure $failures 'Capture/session metadata handoff has not been verified.'
}
if (-not $CentralApiUploadVerified) {
    Add-Failure $failures 'Central API durable upload has not been verified.'
}
if (-not $CatalogVisibilityVerified) {
    Add-Failure $failures 'Authoritative catalog visibility independent of workstation cache has not been verified.'
}
if (-not $BackupProtectedVerified) {
    Add-Failure $failures 'P06 verified Backup protection has not been verified.'
}
if (-not $RestartRecoveryVerified) {
    Add-Failure $failures 'Restart/interruption recovery has not been verified.'
}
if (-not $NetworkRecoveryVerified) {
    Add-Failure $failures 'Network-loss/retry recovery has not been verified.'
}
if (-not $CacheCleanupVerified) {
    Add-Failure $failures 'Safe temporary-cache cleanup has not been verified.'
}
if (-not $RtlLtrUxVerified) {
    Add-Failure $failures 'Arabic RTL + English LTR Windows capture UX has not been verified on the target workstation.'
}
if (-not $SecurityBoundaryVerified) {
    Add-Failure $failures 'Client SQL/storage/security boundary has not been verified on the target workflow.'
}
if (-not $OwnerSiteAccepted) {
    Add-Failure $failures 'Owner/site acceptance has not been explicitly recorded.'
}
if ([string]::IsNullOrWhiteSpace($OwnerSiteAcceptanceReference)) {
    Add-Failure $failures 'OwnerSiteAcceptanceReference is required for a PASS result.'
}

if ($PrimaryLength -ne $captureItem.Length) {
    Add-Failure $failures "Primary length ($PrimaryLength) does not equal finalized capture length ($($captureItem.Length))."
}
if ($primaryHash -ne $captureHash) {
    Add-Failure $failures 'Primary SHA-256 does not equal finalized capture SHA-256.'
}
if ($BackupLength -ne $captureItem.Length) {
    Add-Failure $failures "Backup length ($BackupLength) does not equal finalized capture length ($($captureItem.Length))."
}
if ($backupHash -ne $captureHash) {
    Add-Failure $failures 'Backup SHA-256 does not equal finalized capture SHA-256.'
}

$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem

$result = [ordered]@{
    schema = 'mam.p07.real-hardware-evidence.v1'
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    status = if ($failures.Count -eq 0) { 'PASS' } else { 'NOT_PASS' }
    workstation = [ordered]@{
        id = $WorkstationId
        computerName = $env:COMPUTERNAME
        manufacturer = $computer.Manufacturer
        model = $computer.Model
        osCaption = $os.Caption
        osVersion = $os.Version
        osBuild = $os.BuildNumber
        powershellVersion = $PSVersionTable.PSVersion.ToString()
    }
    hardware = [ordered]@{
        provider = $Provider
        deviceId = $DeviceId
        driverVersion = $DriverVersion
        captureCardModel = $CaptureCardModel
        tapeDeckModel = $TapeDeckModel
        input = $InputName
    }
    profile = [ordered]@{
        video = $VideoProfile
        audio = $AudioProfile
        timecodeSource = $TimecodeSource
        container = $Container
        codec = $Codec
    }
    source = [ordered]@{
        tapeId = $TapeId
        normalizedTimecode = $NormalizedTimecode
    }
    finalizedCapture = [ordered]@{
        fileName = $captureItem.Name
        durationSeconds = $CaptureDurationSeconds
        length = $captureItem.Length
        sha256 = $captureHash
        droppedFrames = $DroppedFrames
        approvedDroppedFrameThreshold = $ApprovedDroppedFrameThreshold
    }
    authoritativePrimary = [ordered]@{
        uploadVerified = [bool]$CentralApiUploadVerified
        catalogVisibilityVerified = [bool]$CatalogVisibilityVerified
        length = $PrimaryLength
        sha256 = $primaryHash
        parityWithFinalizedCapture = (($PrimaryLength -eq $captureItem.Length) -and ($primaryHash -eq $captureHash))
    }
    verifiedBackup = [ordered]@{
        protectedVerified = [bool]$BackupProtectedVerified
        length = $BackupLength
        sha256 = $backupHash
        parityWithFinalizedCapture = (($BackupLength -eq $captureItem.Length) -and ($backupHash -eq $captureHash))
    }
    physicalChecks = [ordered]@{
        deviceDiscoveryVerified = [bool]$DeviceDiscoveryVerified
        unsupportedProfileFailClosedVerified = [bool]$UnsupportedProfileFailClosedVerified
        preflightVerified = [bool]$PreflightVerified
        previewVerified = [bool]$PreviewVerified
        previewUnavailableAccepted = [bool]$PreviewUnavailableAccepted
        audioMetersVerified = [bool]$AudioMetersVerified
        audioMetersUnavailableAccepted = [bool]$AudioMetersUnavailableAccepted
        timecodeVerified = [bool]$TimecodeVerified
        timecodeUnavailableAccepted = [bool]$TimecodeUnavailableAccepted
        recordFinalizeVerified = [bool]$RecordFinalizeVerified
        captureMetadataVerified = [bool]$CaptureMetadataVerified
        restartRecoveryVerified = [bool]$RestartRecoveryVerified
        networkRecoveryVerified = [bool]$NetworkRecoveryVerified
        cacheCleanupVerified = [bool]$CacheCleanupVerified
        rtlLtrUxVerified = [bool]$RtlLtrUxVerified
        securityBoundaryVerified = [bool]$SecurityBoundaryVerified
        ownerSiteAccepted = [bool]$OwnerSiteAccepted
        operatorName = $OperatorName
        ownerSiteAcceptanceReference = $OwnerSiteAcceptanceReference
    }
    failures = @($failures)
}

$jsonPath = Join-Path $resolvedEvidence 'P07_REAL_HARDWARE_EVIDENCE.json'
$shaPath = Join-Path $resolvedEvidence 'P07_REAL_HARDWARE_SHA256SUMS.txt'

$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$jsonHash = (Get-FileHash -LiteralPath $jsonPath -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    "$captureHash  $($captureItem.Name)",
    "$jsonHash  $(Split-Path -Leaf $jsonPath)"
) | Set-Content -LiteralPath $shaPath -Encoding UTF8

Write-Host "P07 real-hardware evidence status: $($result.status)"
Write-Host "Evidence JSON: $jsonPath"
Write-Host "SHA256 manifest: $shaPath"
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "FAIL: $failure" }
    exit 2
}

Write-Host 'PASS: evidence is internally consistent. Commit only a sanitized summary/hash record; do not commit production media or secrets.'
exit 0
