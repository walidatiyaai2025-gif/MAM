using System.Text.Json;
using MAM.Application.Capture;

namespace MAM.Infrastructure.Capture;

public sealed record CaptureRecoveryManifest(
    Guid SessionId,
    string TapeId,
    string ArtifactPath,
    CaptureSessionState State,
    long ObservedLength,
    long DroppedFrames,
    DateTimeOffset UpdatedUtc,
    Guid? UploadSessionId = null,
    Guid? AssetId = null,
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed class CaptureRecoveryManifestStore
{
    private readonly string _root;

    public CaptureRecoveryManifestStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Recovery root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(CaptureRecoveryManifest manifest, CancellationToken cancellationToken = default)
    {
        var path = GetPath(manifest.SessionId);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temp, path, true);
    }

    public async Task<CaptureRecoveryManifest?> ReadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CaptureRecoveryManifest>(stream, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<CaptureRecoveryManifest>> ListAsync(CancellationToken cancellationToken = default)
    {
        var manifests = new List<CaptureRecoveryManifest>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.capture.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var manifest = await JsonSerializer.DeserializeAsync<CaptureRecoveryManifest>(stream, cancellationToken: cancellationToken);
                if (manifest is not null) manifests.Add(manifest);
            }
            catch (JsonException)
            {
                // Preserve corrupt manifests for forensic recovery; never silently delete them.
            }
        }
        return manifests.OrderByDescending(static item => item.UpdatedUtc).ToArray();
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(Guid sessionId)
    {
        var path = Path.GetFullPath(Path.Combine(_root, $"{sessionId:N}.capture.json"));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery manifest escaped the configured cache root.");
        return path;
    }
}
