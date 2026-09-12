using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MAM.Application.Capture;

namespace MAM.Infrastructure.Capture;

/// <summary>
/// CI/development-only deterministic capture provider. This provider is never evidence of
/// real hardware certification and must not be used to satisfy the P07 physical-device gate.
/// </summary>
public sealed class SimulatedCaptureProvider : ICaptureProvider
{
    private sealed record Session(CaptureSessionRequest Request, DateTimeOffset StartedUtc, string ArtifactPath);
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public Task<IReadOnlyList<CaptureDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CaptureDeviceDescriptor> devices =
        [
            new("Simulator", "sim-001", "MAM Capture Simulator", ["SDI-1"],
                ["1080i50-10bit"], ["pcm-48k-24bit-2ch"], ["LTC", "Embedded"])
        ];
        return Task.FromResult(devices);
    }

    public Task<CaptureSessionSnapshot> StartAsync(CaptureSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.Provider, "Simulator", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Simulator provider requires Provider=Simulator.");
        if (!string.Equals(request.DeviceId, "sim-001", StringComparison.Ordinal))
            throw new InvalidOperationException("Unknown simulated capture device.");
        if (string.IsNullOrWhiteSpace(request.TapeId))
            throw new InvalidOperationException("TapeId is required.");

        var cacheRoot = Path.GetFullPath(request.TemporaryCachePath);
        Directory.CreateDirectory(cacheRoot);
        var sessionId = Guid.NewGuid();
        var artifact = Path.Combine(cacheRoot, $"capture-{sessionId:N}.mxf");
        _sessions[sessionId] = new(request, DateTimeOffset.UtcNow, artifact);
        return Task.FromResult(Snapshot(sessionId, CaptureSessionState.Recording, TimeSpan.Zero, 0));
    }

    public Task<CaptureSessionSnapshot> GetStatusAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Capture session {sessionId} was not found.");
        var elapsed = DateTimeOffset.UtcNow - session.StartedUtc;
        return Task.FromResult(Snapshot(sessionId, CaptureSessionState.Recording, elapsed, 0));
    }

    public async Task<CaptureFinalizeResult> StopAndFinalizeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryRemove(sessionId, out var session))
            throw new KeyNotFoundException($"Capture session {sessionId} was not found.");

        var payload = Encoding.UTF8.GetBytes($"MAM-P07-SIMULATED-CAPTURE\nSESSION={sessionId:D}\nTAPE={session.Request.TapeId}\nPROFILE={session.Request.VideoProfile}\n");
        await File.WriteAllBytesAsync(session.ArtifactPath, payload, cancellationToken);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new(sessionId, CaptureSessionState.ReadyForUpload, session.ArtifactPath,
            payload.LongLength, sha, 0, null, null);
    }

    private static CaptureSessionSnapshot Snapshot(Guid sessionId, CaptureSessionState state, TimeSpan duration, long droppedFrames)
    {
        var totalFrames = Math.Max(0, (long)(duration.TotalSeconds * 25));
        var ff = totalFrames % 25;
        var totalSeconds = totalFrames / 25;
        var ss = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var mm = totalMinutes % 60;
        var hh = (totalMinutes / 60) % 24;
        return new(sessionId, state, duration, $"{hh:00}:{mm:00}:{ss:00}:{ff:00}", droppedFrames,
            [-18.0, -17.5], null, null);
    }
}
