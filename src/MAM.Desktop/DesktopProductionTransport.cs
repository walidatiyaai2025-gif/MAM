using System.Net;
using System.Net.Http;

namespace MAM.Desktop;

internal static class DesktopProductionTransport
{
    public const string ProductionEnvironment = "Production";
    public const string ProductionOrigin = "https://mam.da.gov.kw/";

    public static bool IsProduction =>
        string.Equals(
            Environment.GetEnvironmentVariable("MAM_DESKTOP_ENVIRONMENT"),
            ProductionEnvironment,
            StringComparison.OrdinalIgnoreCase);

    public static string? DevelopmentUser =>
        IsProduction ? null : NullIfBlank(Environment.GetEnvironmentVariable("MAM_DEV_USER"));

    public static HttpClient? CreateApiClient(TimeSpan timeout)
    {
        var configured = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
            return null;

        if (IsProduction)
        {
            var webBase = Environment.GetEnvironmentVariable("MAM_WEB_BASE_URL");
            if (!Uri.TryCreate(webBase, UriKind.Absolute, out var webUri))
                webUri = new Uri(ProductionOrigin, UriKind.Absolute);

            ValidateProductionOrigin(webUri);
            var windowsHandler = new HttpClientHandler
            {
                UseDefaultCredentials = true,
                PreAuthenticate = true,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };
            var gateway = new ProductionWebGatewayHandler(webUri, windowsHandler);
            return new HttpClient(gateway)
            {
                BaseAddress = EnsureTrailingSlash(webUri),
                Timeout = timeout
            };
        }

        return new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(configuredUri),
            Timeout = timeout
        };
    }

    public static string EnvironmentLabel =>
        IsProduction ? ProductionEnvironment : Environment.GetEnvironmentVariable("MAM_DESKTOP_ENVIRONMENT") ?? "Development";

    private static void ValidateProductionOrigin(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("mam.da.gov.kw", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort)
        {
            throw new InvalidOperationException(
                $"Production Desktop is locked to {ProductionOrigin}. Configured gateway '{uri}' is not allowed.");
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ProductionWebGatewayHandler : DelegatingHandler
    {
        private readonly Uri _origin;
        private readonly SemaphoreSlim _sessionLock = new(1, 1);
        private volatile bool _sessionReady;

        public ProductionWebGatewayHandler(Uri origin, HttpMessageHandler innerHandler) : base(innerHandler) =>
            _origin = EnsureTrailingSlash(origin);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await EnsureProductionSessionAsync(cancellationToken);

            if (request.RequestUri is not null)
                request.RequestUri = RewriteToWebGateway(request.RequestUri);

            request.Headers.Remove("X-MAM-Dev-User");
            request.Headers.TryAddWithoutValidation("X-MAM-Client", "WindowsDesktopProduction");
            return await base.SendAsync(request, cancellationToken);
        }

        private async Task EnsureProductionSessionAsync(CancellationToken cancellationToken)
        {
            if (_sessionReady) return;

            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                if (_sessionReady) return;

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    new Uri(_origin, "auth/windows?returnUrl=%2Fauth%2Fstatus"));
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await base.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Production Windows SSO failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                }

                _sessionReady = true;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private Uri RewriteToWebGateway(Uri requestUri)
        {
            var path = requestUri.IsAbsoluteUri
                ? requestUri.AbsolutePath.TrimStart('/')
                : requestUri.OriginalString.TrimStart('/');

            var query = requestUri.IsAbsoluteUri ? requestUri.Query : string.Empty;
            string gatewayPath;
            if (path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
                gatewayPath = "client-api/" + path["api/v1/".Length..];
            else if (path.StartsWith("health/", StringComparison.OrdinalIgnoreCase))
                gatewayPath = "client-api/" + path;
            else if (path.StartsWith("client-api/", StringComparison.OrdinalIgnoreCase))
                gatewayPath = path;
            else
                throw new InvalidOperationException($"Production Desktop request path is not gateway-safe: {path}");

            return new Uri(_origin, gatewayPath + query);
        }
    }
}
