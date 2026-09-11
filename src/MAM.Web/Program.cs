using MAM.Application.Branding;
using MAM.Application.Clients;
using MAM.Application.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var build = BuildInfo.Current;

MamCatalogApiClient? catalogClient = null;
var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
if (Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
{
    var http = new HttpClient
    {
        BaseAddress = EnsureTrailingSlash(apiUri),
        Timeout = TimeSpan.FromSeconds(20)
    };
    catalogClient = new MamCatalogApiClient(http, "WebPortal", Environment.GetEnvironmentVariable("MAM_DEV_USER"));
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/version", () => Results.Ok(build));
app.MapGet(BrandTokens.CrestRuntimePath, () =>
{
    if (!DiwanCrestData.HasApprovedFingerprint())
        return Results.Problem("Brand asset fingerprint validation failed.", statusCode: StatusCodes.Status500InternalServerError);
    return Results.File(DiwanCrestData.Bytes.ToArray(), "image/png");
});

app.MapGet("/client-api/status", () => Results.Ok(new { configured = catalogClient is not null }));
app.MapGet("/client-api/catalog/assets", async (CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await catalogClient.ListAssetsAsync(cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (TaskCanceledException) { return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapPost("/client-api/catalog/assets", async (WebCreateAssetRequest request, CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await catalogClient.CreateAssetAsync(request.Title, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (TaskCanceledException) { return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapPatch("/client-api/catalog/assets/{assetId:guid}/title", async (
    Guid assetId,
    WebUpdateTitleRequest request,
    CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await catalogClient.UpdateTitleAsync(assetId, request.Title, request.ExpectedVersion, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (TaskCanceledException) { return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/client-api/metadata/schemas", async (CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    try { return Results.Ok(await catalogClient.ListMetadataSchemasAsync(cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (TaskCanceledException) { return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult ApiFailure(MamApiException ex) =>
    Results.Json(new { error = "central_api_error", status = (int)ex.StatusCode }, statusCode: (int)ex.StatusCode);

static Uri EnsureTrailingSlash(Uri uri) =>
    uri.AbsoluteUri.EndsWith('/', StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

internal sealed record WebCreateAssetRequest(string Title);
internal sealed record WebUpdateTitleRequest(string Title, long ExpectedVersion);
