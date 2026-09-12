# P07 — Real Hardware Acceptance Package

Status: **OWNER_LAST / DEFERRED_EXTERNAL — READY TO EXECUTE**

This package is the final site/owner acceptance action required by the P07 exit gate. It does not replace or weaken the phase gate. Simulator/mock evidence is not accepted here.

## Preconditions

Before execution, record the approved physical configuration:

- Windows workstation identity and OS build.
- Capture provider/adapter name and version.
- Tape deck exact manufacturer/model.
- Capture card/interface exact manufacturer/model.
- Driver/runtime exact version.
- Input selected on the capture device.
- Approved video profile.
- Approved audio profile.
- Approved timecode source.
- Container and codec.
- Tape/source identifier.
- Representative sustained-capture duration approved by owner/site; no duration is fabricated by CI.
- Explicit approved dropped-frame threshold. Do not assume a threshold; if none is approved, P07 remains blocked.
- Central API endpoint/environment identity without storing credentials in evidence.
- Primary Storage target identity and Backup target identity as operator-safe names only; never record credentials or filesystem secrets.

## Required physical test

Use a representative real tape and perform a sustained capture long enough to exercise normal operational behavior. The site/owner decides the representative duration and records it in the evidence manifest.

During the run verify and retain evidence for all of the following:

1. Device is discovered and selected through the Windows capture provider.
2. Unsupported device/profile conditions fail closed.
3. Preflight explicitly reports device, profile, temporary-cache capacity and Central API state.
4. Live preview is visible and stable on the approved path, or an explicit approved unavailable/error state is demonstrated.
5. Audio meters/channel state are visible and plausible for the source, or an explicit approved unavailable/error state is demonstrated.
6. Timecode is acquired and normalized, or an explicit approved unavailable/error state is demonstrated.
7. Record → stop → finalize completes without silent loss.
8. Capture/session metadata includes workstation/provider/device/input/profile/tape identity.
9. Dropped-frame count is recorded and is less than or equal to the explicitly approved threshold.
10. Final temporary capture artifact has non-zero length and SHA-256 before upload handoff.
11. Central API durable upload completes and authoritative Primary length/SHA-256 equal the finalized capture evidence.
12. The catalog exposes the resulting authoritative asset without dependence on the workstation cache.
13. The asset enters the P06 protection workflow and reaches verified Backup protection against the required site Backup target; Backup length/SHA-256 equal the authoritative original.
14. A restart/interruption recovery case is exercised and preserves recoverable evidence without duplicate/corrupt promotion.
15. A network-loss/retry case is exercised where operationally safe and does not create duplicate/corrupt promotion.
16. Temporary cache cleanup happens only after the required safe-handoff/protection evidence exists under the approved operational policy.
17. Arabic RTL and English LTR Windows capture UX remains usable for preflight, recording, finalizing, uploading, recovery, degraded/error and permission states.
18. No direct SQL/Primary/Backup credential or storage-path bypass is introduced on the Windows client.
19. Owner/site acceptance is explicitly recorded with operator identity and an acceptance reference.

## Evidence to retain

Store evidence outside the repository if it contains site-sensitive values. The repository may contain only sanitized summaries and hashes.

Minimum evidence set:

- completed JSON output from `eng/p07-real-hardware-evidence.ps1`;
- capture-provider/device/driver identification screenshot or sanitized log;
- preflight screenshot/log;
- live-preview screenshot/log, or sanitized evidence of the explicitly accepted unavailable/error path;
- audio-meter screenshot/log, or sanitized evidence of the explicitly accepted unavailable/error path;
- timecode screenshot/log, or sanitized evidence of the explicitly accepted unavailable/error path;
- record/finalize result showing representative duration and dropped-frame count;
- finalized capture artifact length and SHA-256;
- Central API upload completion evidence;
- authoritative Primary length/SHA-256 evidence;
- normal catalog visibility evidence;
- P06 Backup protection result with Backup length/SHA-256 parity;
- restart/network recovery evidence;
- cache cleanup evidence;
- Arabic RTL + English LTR operator UX evidence;
- sanitized security-boundary evidence;
- operator/site acceptance name/date/reference.

Do not commit production media, credentials, API keys, database connection strings, storage roots, personal data or sensitive site screenshots.

## Evidence collector

The validator is compatible with Windows PowerShell 5.1+ and PowerShell 7+ on Windows. Run it on the approved capture workstation after the physical test.

### Example: preview/audio/timecode are available and verified

```powershell
./eng/p07-real-hardware-evidence.ps1 `
  -CaptureFile "D:\MAM-Ingest\session-final.mxf" `
  -EvidenceDirectory "D:\MAM-Evidence\P07" `
  -WorkstationId "CAPTURE-01" `
  -Provider "APPROVED_PROVIDER" `
  -DeviceId "APPROVED_DEVICE_ID" `
  -TapeId "TAPE-001" `
  -VideoProfile "APPROVED_VIDEO_PROFILE" `
  -AudioProfile "APPROVED_AUDIO_PROFILE" `
  -TimecodeSource "APPROVED_TIMECODE_SOURCE" `
  -CaptureDurationSeconds 1800 `
  -DroppedFrames 0 `
  -ApprovedDroppedFrameThreshold 0 `
  -PrimaryLength 123456789 `
  -PrimarySha256 "<64-hex-sha256>" `
  -BackupLength 123456789 `
  -BackupSha256 "<64-hex-sha256>" `
  -DriverVersion "1.2.3" `
  -CaptureCardModel "APPROVED_CAPTURE_CARD" `
  -TapeDeckModel "APPROVED_TAPE_DECK" `
  -InputName "SDI" `
  -Container "MXF" `
  -Codec "APPROVED_CODEC" `
  -OperatorName "SITE_OPERATOR" `
  -OwnerSiteAcceptanceReference "P07-UAT-001" `
  -DeviceDiscoveryVerified `
  -UnsupportedProfileFailClosedVerified `
  -PreflightVerified `
  -PreviewVerified `
  -AudioMetersVerified `
  -TimecodeVerified `
  -RecordFinalizeVerified `
  -CaptureMetadataVerified `
  -CentralApiUploadVerified `
  -CatalogVisibilityVerified `
  -BackupProtectedVerified `
  -RestartRecoveryVerified `
  -NetworkRecoveryVerified `
  -CacheCleanupVerified `
  -RtlLtrUxVerified `
  -SecurityBoundaryVerified `
  -OwnerSiteAccepted
```

### Explicit unavailable/error behavior

For preview, audio meters or timecode, the exit gate permits an explicitly demonstrated and approved unavailable/error behavior where applicable. Use exactly one state per capability. For example, if the approved hardware path does not provide timecode but the approved unavailable state is demonstrated, use `-TimecodeUnavailableAccepted` instead of `-TimecodeVerified`.

Supported mutually exclusive pairs are:

- `-PreviewVerified` or `-PreviewUnavailableAccepted`
- `-AudioMetersVerified` or `-AudioMetersUnavailableAccepted`
- `-TimecodeVerified` or `-TimecodeUnavailableAccepted`

The validator rejects both switches from the same pair because that evidence would be contradictory.

## PASS rule

P07 real-hardware acceptance may be recorded as PASS only when all exit-gate evidence is present and internally consistent. The collector intentionally returns a non-PASS result when, among other conditions:

- the capture artifact is missing/empty;
- representative capture duration is not positive;
- dropped-frame count is negative or exceeds the explicitly approved threshold;
- real device discovery, unsupported/degraded fail-closed behavior or preflight is not verified;
- preview/audio/timecode has neither a verified path nor an explicitly approved unavailable/error path;
- record/finalize or capture metadata evidence is missing;
- Primary hash/length differ from finalized capture evidence;
- Central API upload or independent catalog visibility is not verified;
- P06 Backup protection is not verified or Backup hash/length differ from finalized capture evidence;
- restart/network recovery or safe cache cleanup is missing;
- Arabic RTL + English LTR target-workstation UX or the client security boundary is not verified;
- owner/site acceptance and its reference are not explicitly recorded.

The generated JSON being internally consistent is necessary but not sufficient by itself: the associated real-device screenshots/logs and owner/site record must substantiate the switches supplied to the validator.

After a real PASS, update `docs/TASK_LEDGER.md`, create `docs/phase-evidence/P07_CLOSURE.md`, verify exact `main` CI, then and only then transition `CURRENT_PHASE.md` to P08.

`UNPUSHED_WORK=NONE`
