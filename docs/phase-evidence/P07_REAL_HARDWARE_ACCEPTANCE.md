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
6. Timecode is acquired and normalized, or the approved unavailable/error behavior is demonstrated.
7. Record → stop → finalize completes without silent loss.
8. Capture/session metadata includes workstation/provider/device/input/profile/tape identity.
9. Dropped-frame count is recorded and is less than or equal to the explicitly approved threshold.
10. Final temporary capture artifact has non-zero length and SHA-256 before upload handoff.
11. Central API durable upload completes and authoritative Primary length/SHA-256 equal the finalized capture evidence.
12. The catalog exposes the resulting authoritative asset without dependence on the workstation cache.
13. The asset enters the P06 protection workflow and reaches verified Backup protection when the required site Backup target is available; Backup length/SHA-256 equal the authoritative original.
14. A restart/interruption recovery case is exercised and preserves recoverable evidence without duplicate/corrupt promotion.
15. A network-loss/retry case is exercised where operationally safe and does not create duplicate/corrupt promotion.
16. Temporary cache cleanup happens only after the required safe-handoff/protection evidence exists under the approved operational policy.
17. Arabic RTL and English LTR Windows capture UX remains usable for preflight, recording, finalizing, uploading, recovery, degraded/error and permission states.
18. No direct SQL/Primary/Backup credential or storage-path bypass is introduced on the Windows client.

## Evidence to retain

Store evidence outside the repository if it contains site-sensitive values. The repository may contain only sanitized summaries and hashes.

Minimum evidence set:

- completed JSON output from `eng/p07-real-hardware-evidence.ps1`;
- capture-provider/device/driver identification screenshot or sanitized log;
- preflight screenshot/log;
- live-preview screenshot where policy permits;
- audio-meter screenshot/log where policy permits;
- timecode screenshot/log;
- record/finalize result showing duration and dropped-frame count;
- finalized capture artifact length and SHA-256;
- Central API upload completion evidence;
- authoritative Primary length/SHA-256 evidence;
- P06 Backup protection result with Backup length/SHA-256 parity;
- restart/network recovery evidence;
- cache cleanup evidence;
- operator/site acceptance name/date/reference.

Do not commit production media, credentials, API keys, database connection strings, storage roots, personal data or sensitive site screenshots.

## Evidence collector

Run from a PowerShell terminal on the approved Windows workstation after the physical test:

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
  -DroppedFrames 0 `
  -ApprovedDroppedFrameThreshold 0 `
  -PrimaryLength 123456789 `
  -PrimarySha256 "<sha256>" `
  -BackupLength 123456789 `
  -BackupSha256 "<sha256>" `
  -PreviewVerified `
  -AudioMetersVerified `
  -TimecodeVerified `
  -CentralApiUploadVerified `
  -BackupProtectedVerified `
  -RestartRecoveryVerified `
  -NetworkRecoveryVerified `
  -CacheCleanupVerified `
  -OwnerSiteAccepted
```

If a capability is explicitly unavailable but accepted by the approved hardware/profile policy, record that rationale in the sanitized owner/site acceptance record instead of falsely setting the corresponding verification switch.

## PASS rule

P07 real-hardware acceptance may be recorded as PASS only when all exit-gate evidence is present and internally consistent. The collector intentionally returns a non-PASS result when:

- the capture artifact is missing/empty;
- no explicit dropped-frame threshold is supplied;
- dropped frames exceed the approved threshold;
- Primary hash/length differ from finalized capture evidence;
- required Backup protection is claimed but Backup hash/length differ;
- required physical/operator verification switches are missing;
- owner/site acceptance is not explicitly recorded.

After a real PASS, update `docs/TASK_LEDGER.md`, create `docs/phase-evidence/P07_CLOSURE.md`, verify exact `main` CI, then and only then transition `CURRENT_PHASE.md` to P08.

`UNPUSHED_WORK=NONE`
