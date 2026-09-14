using System.Net.Http.Headers;
using MAM.Application.Branding;
using MAM.Application.Diagnostics;
using MAM.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);
var authMode = Environment.GetEnvironmentVariable("MAM_AUTH_MODE") ?? "Local";
var activeDirectory = string.Equals(authMode, "ActiveDirectory", StringComparison.OrdinalIgnoreCase);
var developmentUser = Environment.GetEnvironmentVariable("MAM_DEV_USER");

builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization();
if (activeDirectory)
{
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

var app = builder.Build();
var build = BuildInfo.Current;
var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

MamWebApiTransport.Initialize(app.Services.GetRequiredService<IHttpContextAccessor>());

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
            await context.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
            return;
        }

        await next();
    });
}

app.UseStaticFiles();

app.MapGet("/", () => Results.File(Path.Combine(webRoot, "landing.html"), "text/html; charset=utf-8"));
app.MapGet("/landing", () => Results.File(Path.Combine(webRoot, "landing.html"), "text/html; charset=utf-8"));
app.MapGet("/app", () => Results.File(Path.Combine(webRoot, "index.html"), "text/html; charset=utf-8"));
app.MapGet("/auth/login", () => Results.Redirect("/app"));
app.MapGet("/auth/status", (HttpContext context) => Results.Ok(new
{
    authenticated = context.User.Identity?.IsAuthenticated == true,
    userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null,
    authMode,
    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
}));

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
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!IsHopByHop(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            if (!IsHopByHop(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");

        if (context.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) return;
        await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }
}

static bool IsPublicPath(PathString path)
{
    if (path == "/" || path == "/landing" || path == "/landing.html" || path == "/landing.css" || path == "/landing.js" || path == "/version" || path == "/auth/status" || path == "/favicon.ico")
        return true;
    return path.StartsWithSegments("/assets/branding", StringComparison.OrdinalIgnoreCase);
}

static bool IsHopByHop(string headerName) => headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
    || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
