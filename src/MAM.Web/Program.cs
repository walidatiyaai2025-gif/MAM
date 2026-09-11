using MAM.Application.Branding;
using MAM.Application.Clients;
using MAM.Application.Diagnostics;
using MAM.Application.Uploads;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var build = BuildInfo.Current;

MamCatalogApiClient? catalogClient = null;
MamUploadApiClient? uploadClient = null;
var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
if (Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
{
    var http = new HttpClient
    {
        BaseAddress = EnsureTrailingSlash(apiUri),
        Timeout = TimeSpan.FromMinutes(10)
    };
    var developmentUser = Environment.GetEnvironmentVariable("MAM_DEV_USER");
    catalogClient = new MamCatalogApiClient(http, "WebPortal", developmentUser);
    uploadClient = new MamUploadApiClient(http, "WebPortal", developmentUser);
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

app.MapGet("/client-api/status", () => Results.Ok(new { configured = catalogClient is not null, uploadConfigured = uploadClient is not null }));
app.MapGet("/client-api/catalog/assets", async (CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return NotConfigured();
    try { return Results.Ok(await catalogClient.ListAssetsAsync(cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPost("/client-api/catalog/assets", async (WebCreateAssetRequest request, CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return NotConfigured();
    try { return Results.Ok(await catalogClient.CreateAssetAsync(request.Title, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPatch("/client-api/catalog/assets/{assetId:guid}/title", async (
    Guid assetId,
    WebUpdateTitleRequest request,
    CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return NotConfigured();
    try { return Results.Ok(await catalogClient.UpdateTitleAsync(assetId, request.Title, request.ExpectedVersion, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/metadata/schemas", async (CancellationToken cancellationToken) =>
{
    if (catalogClient is null) return NotConfigured();
    try { return Results.Ok(await catalogClient.ListMetadataSchemasAsync(cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPost("/client-api/uploads/sessions", async (CreateUploadSessionRequest request, CancellationToken cancellationToken) =>
{
    if (uploadClient is null) return NotConfigured();
    try { return Results.Ok(await uploadClient.CreateSessionAsync(request, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/uploads/sessions/{sessionId:guid}", async (Guid sessionId, CancellationToken cancellationToken) =>
{
    if (uploadClient is null) return NotConfigured();
    try { return Results.Ok(await uploadClient.GetSessionAsync(sessionId, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPut("/client-api/uploads/sessions/{sessionId:guid}/chunks", async (
    Guid sessionId,
    long offset,
    HttpRequest request,
    CancellationToken cancellationToken) =>
{
    if (uploadClient is null) return NotConfigured();
    var chunkSha = request.Headers["X-Chunk-SHA256"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(chunkSha))
        return Results.BadRequest(new { error = "chunk_sha256_required" });
    try { return Results.Ok(await uploadClient.PutChunkAsync(sessionId, offset, chunkSha, request.Body, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPost("/client-api/uploads/sessions/{sessionId:guid}/finalize", async (Guid sessionId, CancellationToken cancellationToken) =>
{
    if (uploadClient is null) return NotConfigured();
    try { return Results.Ok(await uploadClient.FinalizeAsync(sessionId, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult ApiFailure(MamApiException ex) =>
    Results.Json(new { error = "central_api_error", status = (int)ex.StatusCode, detail = ex.Message }, statusCode: (int)ex.StatusCode);
static IResult NotConfigured() =>
    Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
static IResult Unreachable() =>
    Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
static IResult Timeout() =>
    Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable);

static Uri EnsureTrailingSlash(Uri uri) =>
    uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

internal sealed record WebCreateAssetRequest(string Title);
internal sealed record WebUpdateTitleRequest(string Title, long ExpectedVersion);
