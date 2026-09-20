using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MAM.Web;

internal static class MamWebApiTransport
{
    private static IHttpContextAccessor? _httpContextAccessor;

    public static void Initialize(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    public static HttpClient Create(Uri baseAddress, TimeSpan timeout)
    {
        if (_httpContextAccessor is null)
            throw new InvalidOperationException("MAM web API transport has not been initialized.");

        var handler = new MamSignedIdentityHandler(_httpContextAccessor)
        {
            InnerHandler = CreateNetworkHandler(baseAddress)
        };

        return new HttpClient(handler)
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = timeout
        };
    }

    private static HttpMessageHandler CreateNetworkHandler(Uri baseAddress)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MAM_API_CONNECT_LOOPBACK"),
                "1",
                StringComparison.OrdinalIgnoreCase))
            return new HttpClientHandler();

        var expectedPort = baseAddress.IsDefaultPort
            ? (baseAddress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : baseAddress.Port;

        var sockets = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
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

internal sealed class MamSignedIdentityHandler : DelegatingHandler
{
    internal const string UserHeader = "X-MAM-Auth-User";
    internal const string TimestampHeader = "X-MAM-Auth-Timestamp";
    internal const string SignatureHeader = "X-MAM-Auth-Signature";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public MamSignedIdentityHandler(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

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

        return base.SendAsync(request, cancellationToken);
    }

    private static string RequestTarget(Uri? requestUri)
    {
        if (requestUri is null) return "/";
        if (requestUri.IsAbsoluteUri) return requestUri.PathAndQuery;
        var value = requestUri.OriginalString;
        return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
    }
}
