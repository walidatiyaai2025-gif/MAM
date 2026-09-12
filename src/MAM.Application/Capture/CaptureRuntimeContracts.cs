namespace MAM.Application.Capture;

public sealed record CapturePreflightRequest(
    bool DeviceAvailable,
    bool ProfileSupported,
    bool CachePathAvailable,
    bool CentralApiReachable,
    long AvailableCacheBytes,
    long RequiredCacheBytes);

public sealed record CapturePreflightDiagnostic(string Code, string Message, bool Blocking);

public sealed record CapturePreflightResult(bool CanRecord, IReadOnlyList<CapturePreflightDiagnostic> Diagnostics)
{
    public static CapturePreflightResult Evaluate(CapturePreflightRequest request)
    {
        var diagnostics = new List<CapturePreflightDiagnostic>();
        if (!request.DeviceAvailable)
            diagnostics.Add(new("capture.device.unavailable", "Capture device is unavailable.", true));
        if (!request.ProfileSupported)
            diagnostics.Add(new("capture.profile.unsupported", "Selected capture profile is unsupported by the device.", true));
        if (!request.CachePathAvailable)
            diagnostics.Add(new("capture.cache.unavailable", "Temporary ingest cache is unavailable.", true));
        if (request.RequiredCacheBytes <= 0)
            diagnostics.Add(new("capture.cache.requirement.invalid", "Required cache capacity must be positive.", true));
        else if (request.AvailableCacheBytes < request.RequiredCacheBytes)
            diagnostics.Add(new("capture.cache.insufficient", "Temporary ingest cache has insufficient free capacity.", true));
        if (!request.CentralApiReachable)
            diagnostics.Add(new("capture.api.degraded", "Central API is currently unreachable. Recording may continue only under an approved recovery policy; automatic authoritative handoff is unavailable.", false));

        return new(!diagnostics.Any(x => x.Blocking), diagnostics);
    }
}

public static class CaptureTimecode
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Replace(';', ':').Split(':');
        if (parts.Length != 4 || parts.Any(p => p.Length != 2 || !int.TryParse(p, out _))) return false;
        var hh = int.Parse(parts[0]);
        var mm = int.Parse(parts[1]);
        var ss = int.Parse(parts[2]);
        var ff = int.Parse(parts[3]);
        if (hh is < 0 or > 23 || mm is < 0 or > 59 || ss is < 0 or > 59 || ff is < 0 or > 99) return false;
        normalized = $"{hh:00}:{mm:00}:{ss:00}:{ff:00}";
        return true;
    }
}

public sealed record CaptureHandoffDescriptor(
    Guid SessionId,
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
    string TemporaryArtifactPath,
    long Length,
    string Sha256,
    long DroppedFrames,
    string? NormalizedTimecode,
    DateTimeOffset FinalizedUtc)
{
    public static CaptureHandoffDescriptor From(
        CaptureSessionRequest request,
        CaptureFinalizeResult result,
        string? timecode,
        DateTimeOffset finalizedUtc)
    {
        if (result.State != CaptureSessionState.ReadyForUpload)
            throw new InvalidOperationException("Capture is not finalized for durable upload handoff.");
        if (result.Length <= 0 || string.IsNullOrWhiteSpace(result.Sha256))
            throw new InvalidOperationException("Capture handoff requires finalized length and SHA-256 evidence.");

        CaptureTimecode.TryNormalize(timecode, out var normalized);
        return new(
            result.SessionId,
            request.WorkstationId,
            request.Provider,
            request.DeviceId,
            request.Input,
            request.VideoProfile,
            request.AudioProfile,
            request.TimecodeSource,
            request.Container,
            request.Codec,
            request.TapeId,
            result.TemporaryArtifactPath,
            result.Length,
            result.Sha256.ToLowerInvariant(),
            result.DroppedFrames,
            string.IsNullOrEmpty(normalized) ? null : normalized,
            finalizedUtc);
    }
}
