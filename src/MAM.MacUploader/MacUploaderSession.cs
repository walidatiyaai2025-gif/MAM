using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MAM.Application.Clients;
using MAM.Application.Processing;
using MAM.Application.Uploads;

namespace MAM.MacUploader;

internal sealed class MacUploaderSession : IDisposable
{
    public static readonly Uri ProductionOrigin = new("https://mam.da.gov.kw/", UriKind.Absolute);

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mxf", ".mov", ".mp4", ".mkv", ".avi", ".webm", ".m4v" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wma" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".rtf", ".txt", ".odt" };

    private readonly HttpClient _http;
    private readonly MamUploadApiClient _uploads;
    private readonly MamProcessingApiClient _processing;
    private readonly Dictionary<string, Guid> _resumeSessions = new(StringComparer.OrdinalIgnoreCase);

    public MacUploaderSession()
    {
        var cookies = new CookieContainer();
        var inner = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        _http = new HttpClient(new ProductionGatewayHandler(ProductionOrigin, inner))
        {
            BaseAddress = ProductionOrigin,
            Timeout = TimeSpan.FromMinutes(20)
        };
        _uploads = new MamUploadApiClient(_http, "MacUploaderProduction");
        _processing = new MamProcessingApiClient(_http, "MacUploaderProduction");
    }

    public string? UserName { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserName);

    public static bool IsSupported(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path)) ||
        AudioExtensions.Contains(Path.GetExtension(path)) ||
        ImageExtensions.Contains(Path.GetExtension(path)) ||
        DocumentExtensions.Contains(Path.GetExtension(path));

    public async Task<string> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var normalizedUser = userName?.Trim() ?? string.Empty;
        if (normalizedUser.Length == 0 || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("User name and password are required.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/ad");
        request.Headers.TryAddWithoutValidation("Origin", ProductionOrigin.GetLeftPart(UriPartial.Authority));
        request.Headers.Referrer = new Uri(ProductionOrigin, "auth/login");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = normalizedUser,
            ["password"] = password,
            ["returnUrl"] = "/auth/status"
        });

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is < 300 or >= 400)
            throw new InvalidOperationException($"Authentication failed with HTTP {(int)response.StatusCode}.");

        var location = response.Headers.Location?.ToString() ?? string.Empty;
        if (location.Contains("/auth/login", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Active Directory authentication failed or this account has no MAM access.");

        using var status = await _http.GetAsync("auth/status", cancellationToken);
        status.EnsureSuccessStatusCode();
        var payload = await status.Content.ReadFromJsonAsync<AuthStatus>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Authentication status was empty.");
        if (!payload.Authenticated || string.IsNullOrWhiteSpace(payload.UserName))
            throw new InvalidOperationException("The server did not establish an authenticated MAM session.");

        UserName = payload.UserName;
        return payload.UserName;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync("auth/logout", content: null, cancellationToken);
        }
        finally
        {
            UserName = null;
            _resumeSessions.Clear();
        }
    }

    public async Task UploadFilesAsync(
        IReadOnlyList<string> files,
        IProgress<MacUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("Sign in before uploading.");
        var valid = files.Where(File.Exists).Where(IsSupported).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (valid.Length == 0) throw new InvalidOperationException("No supported media files were selected.");

        long totalBytes = valid.Sum(path => new FileInfo(path).Length);
        long completedBytes = 0;

        for (var index = 0; index < valid.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = valid[index];
            var file = new FileInfo(path);
            var sha = await ComputeSha256Async(path, cancellationToken);

            progress?.Report(new MacUploadProgress(
                index + 1, valid.Length, file.Name, 0, file.Length,
                Percent(completedBytes, totalBytes), "Preparing resumable upload"));

            UploadSessionSnapshot session;
            if (_resumeSessions.TryGetValue(path, out var sessionId))
            {
                try
                {
                    session = await _uploads.GetSessionAsync(sessionId, cancellationToken);
                    if (session.Session.ExpectedLength != file.Length ||
                        !string.Equals(session.Session.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase) ||
                        session.State is UploadSessionState.Expired or UploadSessionState.Failed or UploadSessionState.Quarantined)
                    {
                        _resumeSessions.Remove(path);
                        session = await CreateSessionAsync(file, sha, cancellationToken);
                    }
                }
                catch
                {
                    _resumeSessions.Remove(path);
                    throw;
                }
            }
            else
            {
                session = await CreateSessionAsync(file, sha, cancellationToken);
            }

            if (session.State == UploadSessionState.Completed)
            {
                completedBytes += file.Length;
                _resumeSessions.Remove(path);
                continue;
            }

            await using var source = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

            var offset = session.ReceivedLength;
            source.Position = offset;
            var buffer = new byte[session.Session.ChunkSizeBytes];

            while (offset < file.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = (int)Math.Min(buffer.Length, file.Length - offset);
                var read = 0;
                while (read < requested)
                {
                    var count = await source.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken);
                    if (count == 0) break;
                    read += count;
                }

                if (read == 0)
                    throw new EndOfStreamException("The local file ended before the declared length.");

                var chunk = buffer.AsMemory(0, read).ToArray();
                var chunkSha = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
                await using var chunkStream = new MemoryStream(chunk, writable: false);
                var receipt = await _uploads.PutChunkAsync(
                    session.Session.SessionId, offset, chunkSha, chunkStream, cancellationToken);
                offset = receipt.ReceivedLength;
                source.Position = offset;

                progress?.Report(new MacUploadProgress(
                    index + 1, valid.Length, file.Name, offset, file.Length,
                    Percent(completedBytes + offset, totalBytes), "Uploading"));
            }

            progress?.Report(new MacUploadProgress(
                index + 1, valid.Length, file.Name, file.Length, file.Length,
                Percent(completedBytes + file.Length, totalBytes), "Server verification"));

            var finalized = await _uploads.FinalizeAsync(session.Session.SessionId, cancellationToken);
            _resumeSessions.Remove(path);

            var finalStage = "Completed";
            try
            {
                await QueueAutomaticProcessingAsync(finalized.AssetId, Path.GetExtension(path), cancellationToken);
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                // Primary promotion is already authoritative and durable at this point.
                // A transient processing-queue failure must never turn a successful upload
                // into a false upload failure for this upload-only client.
                finalStage = "Uploaded; automatic processing queue is pending";
            }

            completedBytes += file.Length;
            progress?.Report(new MacUploadProgress(
                index + 1, valid.Length, file.Name, file.Length, file.Length,
                Percent(completedBytes, totalBytes), finalStage));
        }
    }

    private async Task<UploadSessionSnapshot> CreateSessionAsync(
        FileInfo file,
        string sha,
        CancellationToken cancellationToken)
    {
        var session = await _uploads.CreateSessionAsync(new CreateUploadSessionRequest(
            Path.GetFileNameWithoutExtension(file.Name),
            file.Name,
            file.Length,
            sha), cancellationToken);
        _resumeSessions[file.FullName] = session.Session.SessionId;
        return session;
    }

    private async Task QueueAutomaticProcessingAsync(Guid assetId, string extension, CancellationToken cancellationToken)
    {
        var profiles = new List<string>();
        if (VideoExtensions.Contains(extension) || AudioExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.TranscriptText);
        }
        else if (ImageExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }
        else if (DocumentExtensions.Contains(extension))
        {
            if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }

        foreach (var profile in profiles)
            await _processing.EnqueueAsync(assetId, profile, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static double Percent(long completed, long total) =>
        total <= 0 ? 0d : Math.Clamp(completed * 100d / total, 0d, 100d);

    public void Dispose() => _http.Dispose();

    private sealed record AuthStatus(
        bool Authenticated,
        string? UserName,
        string? AuthenticationType,
        string? AuthMode,
        string? Environment);

    private sealed class ProductionGatewayHandler(Uri origin, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly Uri _origin = origin;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                var path = request.RequestUri.IsAbsoluteUri
                    ? request.RequestUri.AbsolutePath.TrimStart('/')
                    : request.RequestUri.OriginalString.Split('?', 2)[0].TrimStart('/');
                var query = request.RequestUri.IsAbsoluteUri
                    ? request.RequestUri.Query
                    : request.RequestUri.OriginalString.Contains('?')
                        ? "?" + request.RequestUri.OriginalString.Split('?', 2)[1]
                        : string.Empty;

                if (path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
                    request.RequestUri = new Uri(_origin, "client-api/" + path["api/v1/".Length..] + query);
                else if (path.StartsWith("health/", StringComparison.OrdinalIgnoreCase))
                    request.RequestUri = new Uri(_origin, "client-api/" + path + query);
            }

            request.Headers.Remove("X-MAM-Dev-User");
            request.Headers.Remove("X-MAM-Client");
            request.Headers.TryAddWithoutValidation("X-MAM-Client", "MacUploaderProduction");
            return base.SendAsync(request, cancellationToken);
        }
    }
}

internal sealed record MacUploadProgress(
    int FileIndex,
    int FileCount,
    string FileName,
    long FileBytes,
    long FileLength,
    double OverallPercent,
    string Stage);
