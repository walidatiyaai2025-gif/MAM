using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using MAM.Application.Branding;
using MAM.Application.Diagnostics;
using MAM.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

const string WebAuthScheme = "MAM-Web";
const string SessionCookieScheme = "MAM-Session";
const string SessionCookieName = "__Host-MAM-Session";

var builder = WebApplication.CreateBuilder(args);
var authMode = Environment.GetEnvironmentVariable("MAM_AUTH_MODE") ?? "Local";
var activeDirectory = string.Equals(authMode, "ActiveDirectory", StringComparison.OrdinalIgnoreCase);
var developmentUser = Environment.GetEnvironmentVariable("MAM_DEV_USER");

builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // MAM is reverse-proxied locally by IIS/ARR in Production. Trust forwarded
    // client/scheme information only from the local proxy so rate limiting and
    // HTTPS origin checks use the real workstation, not 127.0.0.1 for everyone.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ad-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

if (activeDirectory)
{
    builder.Services.AddSingleton<ActiveDirectoryCredentialValidator>();
    builder.Services
        .AddAuthentication(options =>
        {
            // Normal application traffic is authenticated only by the MAM session
            // cookie. Windows Negotiate is opt-in and used only by /auth/windows.
            options.DefaultAuthenticateScheme = SessionCookieScheme;
            options.DefaultChallengeScheme = SessionCookieScheme;
            options.DefaultSignInScheme = SessionCookieScheme;
        })
        .AddCookie(SessionCookieScheme, options =>
        {
            options.Cookie.Name = SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.LoginPath = "/auth/login";
            options.AccessDeniedPath = "/auth/login";
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = redirectContext =>
                {
                    if (IsApiStyleRequest(redirectContext.Request))
                    {
                        redirectContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        redirectContext.Response.Headers.CacheControl = "no-store";
                        return Task.CompletedTask;
                    }

                    redirectContext.Response.Redirect(redirectContext.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = redirectContext =>
                {
                    if (IsApiStyleRequest(redirectContext.Request))
                    {
                        redirectContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        redirectContext.Response.Headers.CacheControl = "no-store";
                        return Task.CompletedTask;
                    }

                    redirectContext.Response.Redirect(redirectContext.RedirectUri);
                    return Task.CompletedTask;
                }
            };
        })
        .AddNegotiate();
}

var app = builder.Build();
var build = BuildInfo.Current;
var runtimeInspector = new RuntimeInspectorLog("MAM.Web", build);
runtimeInspector.Write(new RuntimeDiagnosticEvent(
    "Information", "process-start",
    $"MAM.Web started. Runtime inspector log root: {runtimeInspector.RootPath}",
    Metadata: new Dictionary<string, string?> { ["logRoot"] = runtimeInspector.RootPath }));
var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var activePresence = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
var presenceWindow = TimeSpan.FromSeconds(90);
Func<HttpContext, string?> presenceIdentity = context =>
{
    if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        return context.User.Identity.Name.Trim();
    if (!activeDirectory && !string.IsNullOrWhiteSpace(developmentUser))
        return developmentUser.Trim();
    if (!activeDirectory)
        return "local:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    return null;
};
Action<DateTimeOffset> prunePresence = now =>
{
    foreach (var entry in activePresence)
    {
        if (now - entry.Value > presenceWindow)
            activePresence.TryRemove(entry.Key, out _);
    }
};

MamWebApiTransport.Initialize(app.Services.GetRequiredService<IHttpContextAccessor>());
app.UseForwardedHeaders();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var supplied)
        && !string.IsNullOrWhiteSpace(supplied.FirstOrDefault())
            ? supplied.First().Trim()
            : Guid.NewGuid().ToString("D");
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

    try
    {
        await next();
        if (context.Response.StatusCode >= 500)
        {
            runtimeInspector.Write(new RuntimeDiagnosticEvent(
                "Error",
                "web-http-response",
                $"HTTP {context.Response.StatusCode} on {context.Request.Method} {context.Request.Path}",
                CorrelationId: correlationId,
                Route: context.Request.Path.Value,
                Method: context.Request.Method,
                Status: context.Response.StatusCode,
                User: context.User.Identity?.Name,
                Metadata: new Dictionary<string, string?>
                {
                    ["elapsedMs"] = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
    }
    catch (Exception ex)
    {
        runtimeInspector.Write(new RuntimeDiagnosticEvent(
            "Error",
            "unhandled-web-exception",
            ex.Message,
            ex.GetType().FullName,
            ex.ToString(),
            correlationId,
            context.Request.Path.Value,
            context.Request.Method,
            StatusCodes.Status500InternalServerError,
            context.User.Identity?.Name));
        throw;
    }
});

if (activeDirectory)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        if (IsPublicPath(context.Request.Path))
        {
            await next();
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            if (IsHtmlNavigation(context.Request))
            {
                var returnUrl = context.Request.PathBase.Add(context.Request.Path).ToString() + context.Request.QueryString;
                context.Response.Redirect("/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "authentication_required",
                detail = "Sign in with Windows SSO or the secure Active Directory login page.",
                login = "/auth/login"
            });
            return;
        }

        await next();
    });
}

app.UseStaticFiles();

app.MapGet("/", () => Results.File(Path.Combine(webRoot, activeDirectory ? "landing.html" : "index.html"), "text/html; charset=utf-8"));
app.MapGet("/landing", () => Results.File(Path.Combine(webRoot, "landing.html"), "text/html; charset=utf-8"));
app.MapGet("/app", () => Results.File(Path.Combine(webRoot, "index.html"), "text/html; charset=utf-8"));
app.MapGet("/auth/login", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (context.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/app");
    return Results.File(Path.Combine(webRoot, "login.html"), "text/html; charset=utf-8");
});

app.MapGet("/auth/windows", async (HttpContext context, string? returnUrl, CancellationToken cancellationToken) =>
{
    var safeReturnUrl = SafeLocalReturnUrl(returnUrl);
    context.Response.Headers.CacheControl = "no-store";

    // Windows authentication is explicit and isolated to this endpoint. The rest
    // of the application never attempts Kerberos/NTLM automatically.
    var windowsResult = await context.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
    if (!windowsResult.Succeeded || windowsResult.Principal is null)
    {
        await context.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
        return;
    }

    var windowsUser = windowsResult.Principal.Identity?.Name?.Trim();
    if (string.IsNullOrWhiteSpace(windowsUser))
    {
        context.Response.Redirect(LoginFailureUrl("windows_identity_missing", safeReturnUrl));
        return;
    }

    var sessionPrincipal = CreateSessionPrincipal(windowsUser, "WindowsSSO");
    var access = await VerifyMamAccessAsync(context, sessionPrincipal, cancellationToken);
    if (!access.Allowed)
    {
        context.Response.Redirect(LoginFailureUrl(access.ErrorCode, safeReturnUrl));
        return;
    }

    await context.SignInAsync(SessionCookieScheme, sessionPrincipal, SessionProperties());
    context.Response.Redirect(safeReturnUrl);
});

app.MapPost("/auth/ad", async (
    HttpContext context,
    ActiveDirectoryCredentialValidator validator,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";

    if (!context.Request.IsHttps || !IsSameOriginFormPost(context.Request))
        return Results.Redirect(LoginFailureUrl("invalid_login_request", "/app"));

    if (!context.Request.HasFormContentType)
        return Results.Redirect(LoginFailureUrl("invalid_login_request", "/app"));

    var form = await context.Request.ReadFormAsync(cancellationToken);
    var safeReturnUrl = SafeLocalReturnUrl(form["returnUrl"].ToString());
    var suppliedUserName = form["username"].ToString();
    var password = form["password"].ToString();

    var validated = validator.Validate(suppliedUserName, password);
    password = string.Empty;

    if (!validated.Success || string.IsNullOrWhiteSpace(validated.CanonicalUserName))
        return Results.Redirect(LoginFailureUrl(validated.ErrorCode, safeReturnUrl));

    var sessionPrincipal = CreateSessionPrincipal(validated.CanonicalUserName, "ActiveDirectoryForm");
    var access = await VerifyMamAccessAsync(context, sessionPrincipal, cancellationToken);
    if (!access.Allowed)
        return Results.Redirect(LoginFailureUrl(access.ErrorCode, safeReturnUrl));

    await context.SignInAsync(SessionCookieScheme, sessionPrincipal, SessionProperties());
    return Results.Redirect(safeReturnUrl);
}).RequireRateLimiting("ad-login");

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    var presenceKey = presenceIdentity(context);
    if (!string.IsNullOrWhiteSpace(presenceKey))
        activePresence.TryRemove(presenceKey, out _);
    await context.SignOutAsync(SessionCookieScheme);
    return Results.Redirect("/");
});

app.MapGet("/auth/status", (HttpContext context) => Results.Ok(new
{
    authenticated = context.User.Identity?.IsAuthenticated == true,
    userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null,
    authenticationType = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.AuthenticationType : null,
    authMode,
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
}));

app.MapPost("/presence/heartbeat", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var presenceKey = presenceIdentity(context);
    if (string.IsNullOrWhiteSpace(presenceKey))
        return Results.Unauthorized();

    var now = DateTimeOffset.UtcNow;
    activePresence[presenceKey] = now;
    prunePresence(now);

    return Results.Ok(new
    {
        activeUsers = activePresence.Count,
        windowSeconds = (int)presenceWindow.TotalSeconds,
        asOfUtc = now
    });
});

app.MapGet("/presence/active", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var now = DateTimeOffset.UtcNow;
    prunePresence(now);
    return Results.Ok(new
    {
        activeUsers = activePresence.Count,
        windowSeconds = (int)presenceWindow.TotalSeconds,
        asOfUtc = now
    });
});

app.MapGet("/version", () => Results.Ok(build));
app.MapGet(BrandTokens.CrestRuntimePath, () =>
{
    if (!DiwanCrestData.HasApprovedFingerprint())
        return Results.Problem("Brand asset fingerprint validation failed.", statusCode: StatusCodes.Status500InternalServerError);
    return Results.File(DiwanCrestData.Bytes.ToArray(), "image/png");
});

app.MapGet("/client-api/status", (HttpContext context) => Results.Ok(new
{
    configured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    uploadConfigured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    processingConfigured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    curationConfigured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    protectionConfigured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    administrationConfigured = Uri.TryCreate(apiBase, UriKind.Absolute, out _),
    authMode,
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
    userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null
}));

var proxyMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" };
app.MapMethods("/client-api/{**path}", proxyMethods, ProxyAsync);

app.MapFallbackToFile("index.html");
app.Run();

async Task<(bool Allowed, string ErrorCode)> VerifyMamAccessAsync(
    HttpContext context,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var baseUri))
        return (false, "central_api_unavailable");

    var originalPrincipal = context.User;
    context.User = principal;
    try
    {
        using var http = MamWebApiTransport.Create(baseUri, TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync("api/v1/session", cancellationToken);
        if (response.IsSuccessStatusCode)
            return (true, string.Empty);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (false, "mam_access_denied");
        return (false, "central_api_unavailable");
    }
    catch (HttpRequestException)
    {
        return (false, "central_api_unavailable");
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return (false, "central_api_unavailable");
    }
    finally
    {
        context.User = originalPrincipal;
    }
}

async Task ProxyAsync(HttpContext context, string? path, CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var baseUri))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "central_api_not_configured", detail = "The Central API base URL is not configured." }, cancellationToken);
        return;
    }

    var normalized = (path ?? string.Empty).Trim('/');
    var targetPath = normalized switch
    {
        "admin/health" => "health/administration",
        "operations/health" => "health/operations",
        "protection/health" => "health/protection",
        _ when normalized.StartsWith("health/", StringComparison.OrdinalIgnoreCase) => normalized,
        _ => "api/v1/" + normalized
    };
    var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;

    using var http = MamWebApiTransport.Create(baseUri, TimeSpan.FromMinutes(10));
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetPath + query);
    request.Headers.TryAddWithoutValidation("X-MAM-Client", "WebPortal");
    if (!activeDirectory && !string.IsNullOrWhiteSpace(developmentUser))
        request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", developmentUser.Trim());

    foreach (var header in new[] { "Accept", "Range", "If-None-Match", "If-Modified-Since", "X-Chunk-SHA256", "X-Correlation-ID" })
    {
        if (context.Request.Headers.TryGetValue(header, out var values))
            request.Headers.TryAddWithoutValidation(header, values.ToArray());
    }

    if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        if (context.Request.ContentLength is long length)
            request.Content.Headers.ContentLength = length;
    }

    HttpResponseMessage response;
    try
    {
        response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "central_api_timeout", detail = "The Central API did not respond before the request timeout." }, cancellationToken);
        return;
    }
    catch (HttpRequestException ex)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "central_api_unreachable", detail = "The Central API could not be reached.", technicalDetail = ex.Message }, cancellationToken);
        return;
    }

    using (response)
    {
        var correlationId = response.Headers.TryGetValues("X-Correlation-ID", out var correlationValues)
            ? correlationValues.FirstOrDefault()
            : null;

        if (activeDirectory &&
            context.User.Identity?.IsAuthenticated == true &&
            response.StatusCode == HttpStatusCode.Unauthorized)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            if (!string.IsNullOrWhiteSpace(correlationId))
                context.Response.Headers["X-Correlation-ID"] = correlationId;

            await context.Response.WriteAsJsonAsync(new
            {
                error = "mam_access_denied",
                detail = "Authentication succeeded, but the Central API rejected this MAM identity. Confirm that the account is enabled in MAM and has at least one role assigned.",
                authenticatedUser = context.User.Identity.Name,
                correlationId
            }, cancellationToken);
            return;
        }

        if (normalized.Equals("admin/health", StringComparison.OrdinalIgnoreCase) && response.IsSuccessStatusCode)
        {
            try
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("administration", out var administration))
                {
                    var isReady = administration.TryGetProperty("isReady", out var readyElement) && readyElement.ValueKind == JsonValueKind.True;
                    var provider = administration.TryGetProperty("provider", out var providerElement) ? providerElement.GetString() : null;
                    var detail = administration.TryGetProperty("detail", out var detailElement) ? detailElement.GetString() : null;
                    var status = json.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : (isReady ? "Ready" : "Degraded");

                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    if (!string.IsNullOrWhiteSpace(correlationId))
                        context.Response.Headers["X-Correlation-ID"] = correlationId;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status,
                        isReady,
                        provider,
                        detail,
                        administration = administration.Clone()
                    }, cancellationToken);
                    return;
                }
            }
            catch (JsonException)
            {
                // Fall back to transparent proxying if an older/newer API returns a different shape.
            }
        }

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!IsHopByHop(header.Key) && !header.Key.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
                context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            if (!IsHopByHop(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");
        context.Response.Headers.Remove("www-authenticate");

        if (context.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) return;
        await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }
}

static ClaimsPrincipal CreateSessionPrincipal(string userName, string authenticationType)
{
    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, userName) },
        authenticationType,
        ClaimTypes.Name,
        ClaimTypes.Role);
    return new ClaimsPrincipal(identity);
}

static AuthenticationProperties SessionProperties() => new()
{
    IsPersistent = false,
    AllowRefresh = true,
    IssuedUtc = DateTimeOffset.UtcNow,
    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
};

static bool IsPublicPath(PathString path)
{
    if (path == "/" ||
        path == "/landing" ||
        path == "/landing.html" ||
        path == "/landing.css" ||
        path == "/landing.js" ||
        path == "/fonts.css" ||
        path == "/login.html" ||
        path == "/auth/login" ||
        path == "/auth/windows" ||
        path == "/auth/ad" ||
        path == "/auth/logout" ||
        path == "/version" ||
        path == "/auth/status" ||
        path == "/favicon.ico")
        return true;
    return path.StartsWithSegments("/assets/branding", StringComparison.OrdinalIgnoreCase);
}

static bool IsApiStyleRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/client-api", StringComparison.OrdinalIgnoreCase) ||
    request.Path.StartsWithSegments("/presence", StringComparison.OrdinalIgnoreCase) ||
    request.Path.StartsWithSegments("/auth/status", StringComparison.OrdinalIgnoreCase) ||
    request.Headers.Accept.Any(value =>
        value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

static bool IsHtmlNavigation(HttpRequest request)
{
    if (!HttpMethods.IsGet(request.Method)) return false;
    if (!request.Headers.TryGetValue("Accept", out var accept)) return false;
    return accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
}

static bool IsSameOriginFormPost(HttpRequest request)
{
    var originValue = request.Headers.Origin.ToString();
    if (!Uri.TryCreate(originValue, UriKind.Absolute, out var origin))
        return false;
    return origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
        && origin.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase);
}

static string LoginFailureUrl(string errorCode, string returnUrl) =>
    "/auth/login?error=" + Uri.EscapeDataString(errorCode) + "&returnUrl=" + Uri.EscapeDataString(SafeLocalReturnUrl(returnUrl));

static string SafeLocalReturnUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return "/app";
    var candidate = value.Trim();
    if (!candidate.StartsWith("/", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal)) return "/app";
    if (candidate.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)) return "/app";
    return candidate;
}

static bool IsHopByHop(string headerName) => headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
