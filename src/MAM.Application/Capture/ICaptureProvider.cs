namespace MAM.Application.Capture;

/// <summary>
/// Vendor-neutral boundary for Windows capture hardware. Implementations belong to
/// the Windows/native integration layer; vendor SDK types must not cross this contract.
/// </summary>
public interface ICaptureProvider
{
    Task<IReadOnlyList<CaptureDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken);
    Task<CaptureSessionSnapshot> StartAsync(CaptureSessionRequest request, CancellationToken cancellationToken);
    Task<CaptureSessionSnapshot> GetStatusAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<CaptureFinalizeResult> StopAndFinalizeAsync(Guid sessionId, CancellationToken cancellationToken);
}

public sealed record CaptureDeviceDescriptor(
    string Provider,
    string DeviceId,
    string DisplayName,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> VideoProfiles,
    IReadOnlyList<string> AudioProfiles,
    IReadOnlyList<string> TimecodeSources);

public sealed record CaptureSessionRequest(
    string WorkstationId,
    string Provider,
    string DeviceId,
    string Input,
    string VideoProfile,
    string AudioProfile,
    string TimecodeSource,
    string Container,
    string Codec,
    string TapeId,
    string TemporaryCachePath);

public enum CaptureSessionState
{
    Starting,
    Recording,
    Stopping,
    Finalizing,
    ReadyForUpload,
    Failed
}

public sealed record CaptureSessionSnapshot(
    Guid SessionId,
    CaptureSessionState State,
    TimeSpan RecordedDuration,
    string? Timecode,
    long DroppedFrames,
    IReadOnlyList<double> AudioPeaksDb,
    string? FailureCode,
    string? FailureMessage);

public sealed record CaptureFinalizeResult(
    Guid SessionId,
    CaptureSessionState State,
    string TemporaryArtifactPath,
    long Length,
    string Sha256,
    long DroppedFrames,
    string? FailureCode,
    string? FailureMessage);
