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
    Task<CapturePreviewFrame> GetPreviewFrameAsync(Guid sessionId, CancellationToken cancellationToken);
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

public enum CapturePreviewState
{
    Available,
    Unavailable,
    Error
}

/// <summary>
/// Normalized preview frame. Available frames use packed BGRA32 pixels so the WPF client
/// never depends on vendor SDK frame types. Unavailable/error states remain explicit.
/// </summary>
public sealed record CapturePreviewFrame(
    CapturePreviewState State,
    int Width,
    int Height,
    int Stride,
    byte[] PixelsBgra32,
    DateTimeOffset CapturedUtc,
    string? FailureCode = null,
    string? FailureMessage = null)
{
    public static CapturePreviewFrame Unavailable(string? detail = null) =>
        new(CapturePreviewState.Unavailable, 0, 0, 0, Array.Empty<byte>(), DateTimeOffset.UtcNow, "capture.preview.unavailable", detail);

    public static CapturePreviewFrame Error(string code, string message) =>
        new(CapturePreviewState.Error, 0, 0, 0, Array.Empty<byte>(), DateTimeOffset.UtcNow, code, message);

    public bool HasUsablePixels =>
        State == CapturePreviewState.Available && Width > 0 && Height > 0 && Stride >= Width * 4 &&
        PixelsBgra32.Length >= checked(Stride * Height);
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
