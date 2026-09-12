using MAM.Application.Branding;
using MAM.Application.Clients;
using MAM.Application.Diagnostics;
using MAM.Application.Uploads;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var build = BuildInfo.Current;

MamCatalogApiClient? catalogClient = null;
MamUploadApiClient? uploadClient = null;
MamProcessingApiClient? processingClient = null;
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
    processingClient = new MamProcessingApiClient(http, "WebPortal", developmentUser);
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

app.MapGet("/client-api/status", () => Results.Ok(new
{
    configured = catalogClient is not null,
    uploadConfigured = uploadClient is not null,
    processingConfigured = processingClient is not null
}));

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

app.MapPatch("/client-api/catalog/assets/{assetId:guid}/title", async (Guid assetId, WebUpdateTitleRequest request, CancellationToken cancellationToken) =>
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

app.MapPut("/client-api/uploads/sessions/{sessionId:guid}/chunks", async (Guid sessionId, long offset, HttpRequest request, CancellationToken cancellationToken) =>
{
    if (uploadClient is null) return NotConfigured();
    var chunkSha = request.Headers["X-Chunk-SHA256"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(chunkSha)) return Results.BadRequest(new { error = "chunk_sha256_required" });
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

app.MapGet("/client-api/processing/profiles", async (CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try { return Results.Ok(await processingClient.ListProfilesAsync(cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/processing/jobs", async (int? limit, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try { return Results.Ok(await processingClient.ListJobsAsync(limit ?? 100, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPost("/client-api/processing/assets/{assetId:guid}/jobs", async (Guid assetId, WebEnqueueProcessingRequest request, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try { return Results.Ok(await processingClient.EnqueueAsync(assetId, request.ProfileId, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapPost("/client-api/processing/jobs/{jobId:guid}/retry", async (Guid jobId, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try { return Results.Ok(await processingClient.RetryAsync(jobId, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/processing/assets/{assetId:guid}/technical", async (Guid assetId, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try
    {
        var technical = await processingClient.GetTechnicalAsync(assetId, cancellationToken);
        return technical is null ? Results.NotFound() : Results.Ok(technical);
    }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/processing/assets/{assetId:guid}/derivatives", async (Guid assetId, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try { return Results.Ok(await processingClient.ListDerivativesAsync(assetId, cancellationToken)); }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/processing/assets/{assetId:guid}/derivatives/{derivativeId:guid}/content", async (Guid assetId, Guid derivativeId, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try
    {
        var download = await processingClient.DownloadDerivativeAsync(assetId, derivativeId, cancellationToken);
        return Results.File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
    }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapGet("/client-api/processing/assets/{assetId:guid}/preview/original", async (Guid assetId, CancellationToken cancellationToken) =>
{
    if (processingClient is null) return NotConfigured();
    try
    {
        var download = await processingClient.DownloadOriginalPreviewAsync(assetId, cancellationToken);
        return Results.File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
    }
    catch (MamApiException ex) { return ApiFailure(ex); }
    catch (HttpRequestException) { return Unreachable(); }
    catch (TaskCanceledException) { return Timeout(); }
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult ApiFailure(MamApiException ex) =>
    Results.Json(new { error = "central_api_error", status = (int)ex.StatusCode, detail = ex.Message }, statusCode: (int)ex.StatusCode);
static IResult NotConfigured() => Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
static IResult Unreachable() => Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
static IResult Timeout() => Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable);
static Uri EnsureTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

internal sealed record WebCreateAssetRequest(string Title);
internal sealed record WebUpdateTitleRequest(string Title, long ExpectedVersion);
internal sealed record WebEnqueueProcessingRequest(string ProfileId);
