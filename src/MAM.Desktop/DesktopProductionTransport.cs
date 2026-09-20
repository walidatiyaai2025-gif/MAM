using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace MAM.Desktop;

internal static class DesktopProductionTransport
{
    public const string ProductionEnvironment = "Production";
    public const string ProductionOrigin = "https://mam.da.gov.kw/";

    private static readonly CookieContainer ProductionCookies = new();
    private static readonly SemaphoreSlim ProductionSessionLock = new(1, 1);
    private static volatile bool _productionSessionReady;
    private static volatile bool _useDefaultCredentialsForGateway = true;

    public static string? AuthenticatedUser { get; private set; }
    public static string AuthenticationLabel { get; private set; } = "Not signed in";

    public static bool IsProduction =>
        string.Equals(
            Environment.GetEnvironmentVariable("MAM_DESKTOP_ENVIRONMENT"),
            ProductionEnvironment,
            StringComparison.OrdinalIgnoreCase);

    public static string? DevelopmentUser =>
        IsProduction ? null : NullIfBlank(Environment.GetEnvironmentVariable("MAM_DEV_USER"));

    public static async Task<string> SignInWithActiveDirectoryAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsProduction)
            throw new InvalidOperationException("Active Directory credential sign-in is available only in Production Desktop.");

        var normalizedUser = userName?.Trim() ?? string.Empty;
        if (normalizedUser.Length == 0 || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("User name and password are required.");

        var origin = new Uri(ProductionOrigin, UriKind.Absolute);
        ValidateProductionOrigin(origin);

        await ProductionSessionLock.WaitAsync(cancellationToken);
        try
        {
            using var handler = CreateProductionHandler(useDefaultCredentials: false, allowAutoRedirect: false);
            using var client = new HttpClient(handler)
            {
                BaseAddress = origin,
                Timeout = TimeSpan.FromSeconds(45)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/ad");
            request.Headers.TryAddWithoutValidation("Origin", origin.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(origin, "auth/login");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = normalizedUser,
                ["password"] = password,
                ["returnUrl"] = "/auth/status"
            });

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is < 300 or >= 400)
                throw new InvalidOperationException(
                    $"Active Directory authentication failed with HTTP {(int)response.StatusCode}.");

            var location = response.Headers.Location?.ToString() ?? string.Empty;
            if (location.Contains("/auth/login", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Active Directory authentication failed or this account has no MAM access.");

            var authenticatedUser = await ReadAuthenticatedUserAsync(client, cancellationToken);
            AuthenticatedUser = authenticatedUser;
            AuthenticationLabel = "AD credentials";
            _useDefaultCredentialsForGateway = false;
            _productionSessionReady = true;
            return authenticatedUser;
        }
        finally
        {
            ProductionSessionLock.Release();
        }
    }

    public static async Task<string> SignInWithWindowsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsProduction)
            throw new InvalidOperationException("Windows SSO is available only in Production Desktop.");

        if (_productionSessionReady && !string.IsNullOrWhiteSpace(AuthenticatedUser))
            return AuthenticatedUser;

        var origin = new Uri(ProductionOrigin, UriKind.Absolute);
        ValidateProductionOrigin(origin);

        await ProductionSessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_productionSessionReady && !string.IsNullOrWhiteSpace(AuthenticatedUser))
                return AuthenticatedUser;

            using var handler = CreateProductionHandler(useDefaultCredentials: true, allowAutoRedirect: true);
            using var client = new HttpClient(handler)
            {
                BaseAddress = origin,
                Timeout = TimeSpan.FromSeconds(45)
            };

            using var response = await client.GetAsync(
                "auth/windows?returnUrl=%2Fauth%2Fstatus",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Production Windows SSO failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

            var authenticatedUser = await ReadAuthenticatedUserAsync(client, cancellationToken);
            AuthenticatedUser = authenticatedUser;
            AuthenticationLabel = "Windows SSO";
            _useDefaultCredentialsForGateway = true;
            _productionSessionReady = true;
            return authenticatedUser;
        }
        finally
        {
            ProductionSessionLock.Release();
        }
    }

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
            var productionHandler = CreateProductionHandler(
                useDefaultCredentials: _useDefaultCredentialsForGateway,
                allowAutoRedirect: true);
            var gateway = new ProductionWebGatewayHandler(webUri, productionHandler);
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

    private static HttpClientHandler CreateProductionHandler(bool useDefaultCredentials, bool allowAutoRedirect)
    {
        if (useDefaultCredentials)
        {
            return new HttpClientHandler
            {
                UseDefaultCredentials = true,
                PreAuthenticate = true,
                UseCookies = true,
                CookieContainer = ProductionCookies,
                AllowAutoRedirect = allowAutoRedirect,
                AutomaticDecompression = DecompressionMethods.All
            };
        }

        return new HttpClientHandler
        {
            UseDefaultCredentials = false,
            PreAuthenticate = false,
            UseCookies = true,
            CookieContainer = ProductionCookies,
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = DecompressionMethods.All
        };
    }

    private static async Task<string> ReadAuthenticatedUserAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var status = await client.GetAsync("auth/status", cancellationToken);
        status.EnsureSuccessStatusCode();
        var payload = await status.Content.ReadFromJsonAsync<AuthStatus>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Authentication status was empty.");

        if (!payload.Authenticated || string.IsNullOrWhiteSpace(payload.UserName))
            throw new InvalidOperationException("The server did not establish an authenticated MAM session.");

        return payload.UserName;
    }

    private sealed record AuthStatus(bool Authenticated, string? UserName);

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

        public ProductionWebGatewayHandler(Uri origin, HttpMessageHandler innerHandler) : base(innerHandler) =>
            _origin = EnsureTrailingSlash(origin);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await EnsureProductionSessionAsync(cancellationToken);

            if (request.RequestUri is not null)
                request.RequestUri = RewriteToWebGateway(request.RequestUri);

            request.Headers.Remove("X-MAM-Dev-User");
            request.Headers.Remove("X-MAM-Client");
            request.Headers.TryAddWithoutValidation("X-MAM-Client", "WindowsDesktopProduction");
            return await base.SendAsync(request, cancellationToken);
        }

        private async Task EnsureProductionSessionAsync(CancellationToken cancellationToken)
        {
            if (_productionSessionReady) return;
            await SignInWithWindowsAsync(cancellationToken);
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
