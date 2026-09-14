using System.Globalization;
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
            InnerHandler = new HttpClientHandler()
        };

        return new HttpClient(handler)
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = timeout
        };
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
