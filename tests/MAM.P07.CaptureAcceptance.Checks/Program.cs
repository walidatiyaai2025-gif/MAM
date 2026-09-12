using System.Security.Cryptography;
using MAM.Application.Capture;
using MAM.Infrastructure.Capture;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var root = Path.Combine(Path.GetTempPath(), "mam-p07-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var blocked = CapturePreflightResult.Evaluate(new(false, false, false, false, 0, 1024));
    Require(!blocked.CanRecord, "Fail-closed preflight must block unavailable device/profile/cache.");
    Require(blocked.Diagnostics.Count(x => x.Blocking) >= 3, "Expected blocking preflight diagnostics.");

    var degraded = CapturePreflightResult.Evaluate(new(true, true, true, false, 1024 * 1024, 1024));
    Require(degraded.CanRecord, "Central API outage alone should be represented as degraded, not silently as healthy.");
    Require(degraded.Diagnostics.Any(x => x.Code == "capture.api.degraded" && !x.Blocking), "Expected explicit API degraded diagnostic.");

    Require(CaptureTimecode.TryNormalize("01:02:03;04", out var normalized) && normalized == "01:02:03:04", "Timecode normalization failed.");
    Require(!CaptureTimecode.TryNormalize("99:99:99:99", out _), "Invalid timecode must fail closed.");

    var provider = new SimulatedCaptureProvider();
    var devices = await provider.GetDevicesAsync(default);
    Require(devices.Count == 1 && devices[0].Provider == "Simulator", "CI simulator discovery failed.");

    var request = new CaptureSessionRequest(
        "ci-workstation", "Simulator", devices[0].DeviceId, "SDI-1", "1080i50-10bit",
        "pcm-48k-24bit-2ch", "LTC", "mxf", "ffv1", "TAPE-CI-001", root);

    var started = await provider.StartAsync(request, default);
    Require(started.State == CaptureSessionState.Recording, "Capture did not enter Recording.");
    Require(started.AudioPeaksDb.Count == 2, "Audio meter state missing.");

    var recovery = new CaptureRecoveryManifestStore(Path.Combine(root, "recovery"));
    await recovery.SaveAsync(new(started.SessionId, request.TapeId, string.Empty, CaptureSessionState.Recording, 0, started.DroppedFrames, DateTimeOffset.UtcNow));
    var afterRestart = new CaptureRecoveryManifestStore(Path.Combine(root, "recovery"));
    var recovered = await afterRestart.ReadAsync(started.SessionId);
    Require(recovered is not null && recovered.SessionId == started.SessionId && recovered.TapeId == request.TapeId,
        "Recovery manifest did not survive store re-instantiation.");
    var recoveredList = await afterRestart.ListAsync();
    Require(recoveredList.Any(x => x.SessionId == started.SessionId), "Recovery manifest enumeration lost the active session.");

    var status = await provider.GetStatusAsync(started.SessionId, default);
    Require(status.State == CaptureSessionState.Recording, "Capture status regression.");
    Require(CaptureTimecode.TryNormalize(status.Timecode, out _), "Provider timecode must be valid when available.");

    var finalized = await provider.StopAndFinalizeAsync(started.SessionId, default);
    Require(finalized.State == CaptureSessionState.ReadyForUpload, "Capture finalization failed.");
    Require(File.Exists(finalized.TemporaryArtifactPath), "Finalized temporary artifact missing.");
    var bytes = await File.ReadAllBytesAsync(finalized.TemporaryArtifactPath);
    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    Require(finalized.Length == bytes.LongLength && finalized.Sha256 == sha, "Finalized length/SHA-256 evidence mismatch.");
    Require(finalized.DroppedFrames == 0, "Simulator acceptance expected zero dropped frames; this is not a production threshold.");

    var handoff = CaptureHandoffDescriptor.From(request, finalized, status.Timecode, DateTimeOffset.UtcNow);
    Require(handoff.Length == finalized.Length && handoff.Sha256 == finalized.Sha256, "Durable upload handoff evidence mismatch.");
    Require(handoff.WorkstationId == request.WorkstationId && handoff.Provider == request.Provider && handoff.DeviceId == request.DeviceId,
        "Capture workstation/provider/device metadata was not carried into the handoff.");
    Require(handoff.Input == request.Input && handoff.VideoProfile == request.VideoProfile && handoff.AudioProfile == request.AudioProfile,
        "Capture input/profile metadata was not carried into the handoff.");
    Require(handoff.TimecodeSource == request.TimecodeSource && handoff.Container == request.Container && handoff.Codec == request.Codec,
        "Capture timecode/container/codec metadata was not carried into the handoff.");

    var uploadSessionId = Guid.NewGuid();
    var assetId = Guid.NewGuid();
    await recovery.SaveAsync(new(started.SessionId, request.TapeId, finalized.TemporaryArtifactPath,
        CaptureSessionState.ReadyForUpload, finalized.Length, finalized.DroppedFrames, DateTimeOffset.UtcNow,
        uploadSessionId, assetId, "network.interrupted", "Synthetic CI recovery evidence"));
    var finalizedManifest = await recovery.ReadAsync(started.SessionId);
    Require(finalizedManifest?.State == CaptureSessionState.ReadyForUpload, "Finalized recovery state not persisted.");
    Require(finalizedManifest?.UploadSessionId == uploadSessionId && finalizedManifest.AssetId == assetId,
        "Durable upload/asset recovery identity was not persisted.");
    Require(finalizedManifest?.FailureCode == "network.interrupted", "Capture/runtime failure evidence was not persisted.");

    await recovery.DeleteAsync(started.SessionId);
    Require(await recovery.ReadAsync(started.SessionId) is null, "Recovery manifest cleanup failed.");

    Console.WriteLine("P07 non-hardware capture acceptance: PASS");
    Console.WriteLine("NOTE: simulator evidence does not satisfy approved real-hardware certification or dropped-frame threshold acceptance.");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}
