using System.Security.Cryptography;
using MAM.Application.Storage;
using MAM.Infrastructure.Configuration;

namespace MAM.Infrastructure.Storage;

public sealed class FileSystemStorageObjectStore : IStorageObjectStore
{
    private readonly string _root;
    private readonly string _provider;
    private readonly long _minimumFreeBytes;
    private readonly int _minimumFreePercent;
    private readonly bool _writeTestOnHealthCheck;

    public FileSystemStorageObjectStore(PrimaryStorageTargetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.Equals(settings.Type, "FileSystem", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.Type, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Primary storage type '{settings.Type}' is not supported by the file-system adapter.");
        }

        TargetId = string.IsNullOrWhiteSpace(settings.Id)
            ? throw new InvalidOperationException("Primary storage target ID is required.")
            : settings.Id.Trim();
        _root = Path.GetFullPath(settings.Root);
        _provider = string.Equals(settings.Type, "Mock", StringComparison.OrdinalIgnoreCase)
            ? "DevelopmentFileSystem"
            : "FileSystem";
        _minimumFreeBytes = checked(settings.MinimumFreeGB * 1024L * 1024L * 1024L);
        _minimumFreePercent = settings.MinimumFreePercent;
        _writeTestOnHealthCheck = settings.WriteTestOnHealthCheck;
    }

    public string TargetId { get; }

    public async Task<StorageWriteResult> WriteAsync(
        string objectKey,
        Stream source,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var expected = NormalizeSha(expectedSha256);
        var path = ResolveObjectPath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new IOException("The server-generated Primary Storage object key already exists.");

        var temporaryPath = path + ".writing-" + Guid.NewGuid().ToString("N");
        long length = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    length += read;
                }
                await destination.FlushAsync(cancellationToken);
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
                throw new InvalidDataException("Primary Storage write checksum did not match the expected SHA-256.");

            File.Move(temporaryPath, path, overwrite: false);
            return new StorageWriteResult(NormalizeObjectKey(objectKey), length, actual);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveObjectPath(objectKey);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public async Task<StorageVerificationResult> VerifyAsync(
        string objectKey,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var path = ResolveObjectPath(objectKey);
        if (!File.Exists(path)) return new StorageVerificationResult(false, false, null, null);

        var expected = NormalizeSha(expectedSha256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var actualBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(actualBytes).ToLowerInvariant();
        var matches = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), actualBytes);
        return new StorageVerificationResult(true, matches, stream.Length, actual);
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(ResolveObjectPath(objectKey));
        return Task.CompletedTask;
    }

    public async Task<StorageTargetHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var root = Path.GetPathRoot(_root) ?? _root;
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var freePercent = drive.TotalSize <= 0 ? 0 : (int)Math.Floor(free * 100d / drive.TotalSize);
            if (free < _minimumFreeBytes || freePercent < _minimumFreePercent)
            {
                return new StorageTargetHealth(false, TargetId, _provider,
                    $"Primary Storage free-space gate failed: {free / (1024L * 1024L * 1024L)} GB / {freePercent}% available.");
            }

            if (_writeTestOnHealthCheck)
            {
                var probeDirectory = Path.Combine(_root, ".health");
                Directory.CreateDirectory(probeDirectory);
                var probe = Path.Combine(probeDirectory, Guid.NewGuid().ToString("N") + ".probe");
                await File.WriteAllTextAsync(probe, "MAM-P03", cancellationToken);
                TryDelete(probe);
            }

            return new StorageTargetHealth(true, TargetId, _provider, "Primary Storage is writable and capacity gates are satisfied.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new StorageTargetHealth(false, TargetId, _provider,
                $"Primary Storage is unavailable: {ex.GetType().Name}.");
        }
    }

    private string ResolveObjectPath(string objectKey)
    {
        var normalized = NormalizeObjectKey(objectKey);
        var combined = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Storage object key escaped the configured Primary Storage root.");
        return combined;
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new ArgumentException("Storage object key is required.", nameof(objectKey));
        var value = objectKey.Replace('\\', '/').Trim('/');
        if (value.Length == 0 || Path.IsPathRooted(value)) throw new InvalidDataException("Storage object key must be relative.");
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Storage object key contains an unsafe path segment.");
        return string.Join('/', segments);
    }

    private static string NormalizeSha(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", nameof(value));
        return normalized;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
