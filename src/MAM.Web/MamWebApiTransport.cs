using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MAM.Web;

internal static class MamWebApiTransport
{
    private static IHttpContextAccessor? _httpContextAccessor;
    private static readonly ConcurrentDictionary<string, HttpMessageInvoker> SharedTransports =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    public static HttpClient Create(Uri baseAddress, TimeSpan timeout)
    {
        if (_httpContextAccessor is null)
            throw new InvalidOperationException("MAM web API transport has not been initialized.");

        var transport = GetSharedTransport(baseAddress);
        var handler = new MamSignedIdentityHandler(_httpContextAccessor, transport);

        // Callers intentionally create/dispose lightweight HttpClient instances.
        // The underlying transport/connection pool is process-wide and is not
        // disposed with each request. This prevents TIME_WAIT/ephemeral-port
        // exhaustion on the local Web -> API proxy path.
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = timeout
        };
    }

    private static HttpMessageInvoker GetSharedTransport(Uri baseAddress)
    {
        var loopback = string.Equals(
            Environment.GetEnvironmentVariable("MAM_API_CONNECT_LOOPBACK"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        var expectedPort = baseAddress.IsDefaultPort
            ? (baseAddress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : baseAddress.Port;

        var key = string.Join(
            "|",
            loopback ? "loopback" : "direct",
            baseAddress.Scheme.ToLowerInvariant(),
            baseAddress.Host.ToLowerInvariant(),
            expectedPort.ToString(CultureInfo.InvariantCulture));

        return SharedTransports.GetOrAdd(
            key,
            _ => new HttpMessageInvoker(CreateNetworkHandler(baseAddress, loopback), disposeHandler: true));
    }

    private static HttpMessageHandler CreateNetworkHandler(Uri baseAddress, bool loopback)
    {
        if (!loopback)
            return new HttpClientHandler();

        var expectedPort = baseAddress.IsDefaultPort
            ? (baseAddress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : baseAddress.Port;

        var sockets = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 256
        };

        sockets.ConnectCallback = async (context, cancellationToken) =>
        {
            if (context.DnsEndPoint.Port != expectedPort)
                throw new HttpRequestException(
                    $"Central API loopback transport refused unexpected port {context.DnsEndPoint.Port}; expected {expectedPort}.");

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(IPAddress.Loopback, expectedPort, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

        return sockets;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}

internal sealed class MamSignedIdentityHandler : HttpMessageHandler
{
    internal const string UserHeader = "X-MAM-Auth-User";
    internal const string TimestampHeader = "X-MAM-Auth-Timestamp";
    internal const string SignatureHeader = "X-MAM-Auth-Signature";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HttpMessageInvoker _transport;

    public MamSignedIdentityHandler(IHttpContextAccessor httpContextAccessor, HttpMessageInvoker transport)
    {
        _httpContextAccessor = httpContextAccessor;
        _transport = transport;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var identity = _httpContextAccessor.HttpContext?.User?.Identity;
        if (identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(identity.Name))
        {
            var encodedKey = Environment.GetEnvironmentVariable("MAM_INTERNAL_AUTH_KEY");
            if (string.IsNullOrWhiteSpace(encodedKey))
                throw new InvalidOperationException("MAM_INTERNAL_AUTH_KEY is required for authenticated WebPortal requests.");

            byte[] key;
            try { key = Convert.FromBase64String(encodedKey); }
            catch (FormatException ex) { throw new InvalidOperationException("MAM_INTERNAL_AUTH_KEY is not valid base64.", ex); }
            if (key.Length < 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidOperationException("MAM_INTERNAL_AUTH_KEY must contain at least 256 bits.");
            }

            var userName = identity.Name.Trim();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var target = RequestTarget(request.RequestUri);
            var canonical = string.Join("\n", userName, timestamp, request.Method.Method.ToUpperInvariant(), target);

            byte[] signature;
            using (var hmac = new HMACSHA256(key))
                signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));

            request.Headers.Remove(UserHeader);
            request.Headers.Remove(TimestampHeader);
            request.Headers.Remove(SignatureHeader);
            request.Headers.TryAddWithoutValidation(UserHeader, userName);
            request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp);
            request.Headers.TryAddWithoutValidation(SignatureHeader, Convert.ToBase64String(signature));

            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(signature);
        }

        return _transport.SendAsync(request, cancellationToken);
    }

    private static string RequestTarget(Uri? requestUri)
    {
        if (requestUri is null) return "/";
        if (requestUri.IsAbsoluteUri) return requestUri.PathAndQuery;
        var value = requestUri.OriginalString;
        return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
    }
}
