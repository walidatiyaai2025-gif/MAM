using System.Security.Claims;
using MAM.Api.Security;
using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Diagnostics;
using MAM.Application.Identity;
using MAM.Application.Metadata;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Application.Uploads;
using MAM.Domain.Assets;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Processing;
using MAM.Infrastructure.Secrets;
using MAM.Infrastructure.Storage;
using MAM.Infrastructure.Uploads;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var configPath = Environment.GetEnvironmentVariable("MAM_CONFIG_PATH")
                 ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Foundation.json");
var mamSettings = MamSettingsLoader.Load(configPath);
var build = BuildInfo.Current.WithEnvironment(mamSettings.Environment.Name);
builder.Services.AddSingleton(mamSettings);
builder.Services.AddSingleton<IMetadataSchemaRegistry, BuiltInMetadataSchemaRegistry>();

builder.Services
    .AddAuthentication(MamAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, MamAuthenticationHandler>(MamAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MamSecurity.CatalogReadPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(MamSecurity.PermissionClaimType, MamPermissions.CatalogRead));
    options.AddPolicy(MamSecurity.CatalogWritePolicy, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(MamSecurity.PermissionClaimType, MamPermissions.CatalogWrite));
    options.AddPolicy(MamSecurity.AuditReadPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(MamSecurity.PermissionClaimType, MamPermissions.AuditRead));
    options.AddPolicy(MamSecurity.AdministrationPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(MamSecurity.PermissionClaimType, MamPermissions.Administration));
});

var secretResolver = new EnvironmentSecretResolver();
var sqlConfigured = secretResolver.TryResolve(mamSettings.Database.ConnectionStringSecretRef, out var sqlConnectionString);
if (sqlConfigured)
{
    var connections = new SqlServerConnectionFactory(
        sqlConnectionString,
        mamSettings.Database.CommandTimeoutSeconds,
        mamSettings.Database.EnableRetryOnFailure);
    builder.Services.AddSingleton(connections);
    builder.Services.AddSingleton<SqlServerMigrationRunner>();
    builder.Services.AddSingleton<IAuditSink, SqlServerAuditSink>();
    builder.Services.AddSingleton<IAssetCatalog, SqlServerAssetCatalog>();
    builder.Services.AddSingleton<IStorageObjectStore>(_ => new FileSystemStorageObjectStore(mamSettings.Storage.Primary));
    builder.Services.AddSingleton<IDurableUploadService, DurableUploadService>();
    builder.Services.AddSingleton<IMediaProcessingService, SqlServerMediaProcessingService>();
}
else if (string.Equals(mamSettings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAuditSink, InMemoryAuditSink>();
    builder.Services.AddSingleton<IAssetCatalog, DevelopmentAssetCatalog>();
    builder.Services.AddSingleton<IDurableUploadService>(_ => new UnavailableDurableUploadService(
        "Durable upload requires the authoritative SQL Server session store. Development catalog fallback does not become upload authority."));
    builder.Services.AddSingleton<IMediaProcessingService>(_ => new UnavailableMediaProcessingService(
        "Media processing requires the authoritative SQL Server job store and verified Primary originals."));
}
else
{
    builder.Services.AddSingleton<IAuditSink, InMemoryAuditSink>();
    builder.Services.AddSingleton<IAssetCatalog>(_ => new UnavailableAssetCatalog(
        "Authoritative SQL Server catalog secret could not be resolved. Non-development catalog access is fail-closed."));
    builder.Services.AddSingleton<IDurableUploadService>(_ => new UnavailableDurableUploadService(
        "Authoritative SQL Server upload session store could not be resolved. Durable upload is fail-closed."));
    builder.Services.AddSingleton<IMediaProcessingService>(_ => new UnavailableMediaProcessingService(
        "Authoritative SQL Server processing job store could not be resolved. Media processing is fail-closed."));
}

var app = builder.Build();

if (sqlConfigured && string.Equals(Environment.GetEnvironmentVariable("MAM_APPLY_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    if (!string.Equals(mamSettings.Database.MigrationMode, "Explicit", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("MAM_APPLY_MIGRATIONS requires Database.MigrationMode=Explicit.");

    var migrationDirectory = Environment.GetEnvironmentVariable("MAM_MIGRATIONS_PATH")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "database", "migrations");
    await app.Services.GetRequiredService<SqlServerMigrationRunner>().ApplyDirectoryAsync(migrationDirectory);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    product = mamSettings.Environment.DisplayNameEn,
    phase = "P04",
    environment = mamSettings.Environment.Name,
    catalogProvider = sqlConfigured ? "SqlServer" : string.Equals(mamSettings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase) ? "DevelopmentMemory" : "Unavailable",
    primaryStorageTarget = mamSettings.Storage.Primary.Id
}));
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/config", () => Results.Ok(new { status = "Healthy", site = mamSettings.Environment.SiteCode }));
app.MapGet("/health/ready", async (IAssetCatalog catalog, CancellationToken cancellationToken) =>
{
    var health = await catalog.GetHealthAsync(cancellationToken);
    return health.IsReady
        ? Results.Ok(new { status = "Ready", catalog = health })
        : Results.Json(new { status = "Degraded", catalog = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/health/storage", async (IDurableUploadService uploads, CancellationToken cancellationToken) =>
{
    var health = await uploads.GetHealthAsync(cancellationToken);
    return health.IsReady
        ? Results.Ok(new { status = "Ready", upload = health })
        : Results.Json(new { status = "Degraded", upload = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/health/processing", async (IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    var health = await processing.GetHealthAsync(cancellationToken);
    return health.IsReady
        ? Results.Ok(new { status = "Ready", processing = health })
        : Results.Json(new { status = "Degraded", processing = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/version", () => Results.Ok(build));

var configuredApiBasePath = string.IsNullOrWhiteSpace(mamSettings.Server.ApiBasePath)
    ? "/api"
    : $"/{mamSettings.Server.ApiBasePath.Trim('/')}";
var api = app.MapGroup($"{configuredApiBasePath}/v1");

api.MapGet("/session", (ClaimsPrincipal principal) => Results.Ok(new
{
    userId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
    displayName = principal.Identity?.Name,
    roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().ToArray(),
    permissions = principal.FindAll(MamSecurity.PermissionClaimType).Select(claim => claim.Value).Distinct().ToArray()
})).RequireAuthorization();

api.MapGet("/metadata/schemas", (IMetadataSchemaRegistry registry) => Results.Ok(registry.List()))
    .RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapPost("/metadata/schemas/{schemaKey}/validate", (
    string schemaKey,
    MetadataValidationRequest request,
    IMetadataSchemaRegistry registry) =>
{
    var errors = registry.Validate(schemaKey, request.Values ?? new Dictionary<string, string?>());
    return errors.Count == 0 ? Results.Ok(new { valid = true }) : Results.BadRequest(new { valid = false, errors });
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapGet("/catalog/assets", async (IAssetCatalog catalog, CancellationToken cancellationToken) =>
{
    var unavailable = await CatalogUnavailableAsync(catalog, cancellationToken);
    if (unavailable is not null) return unavailable;
    return Results.Ok(await catalog.ListAsync(cancellationToken));
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/catalog/assets/{assetId:guid}", async (Guid assetId, IAssetCatalog catalog, CancellationToken cancellationToken) =>
{
    var unavailable = await CatalogUnavailableAsync(catalog, cancellationToken);
    if (unavailable is not null) return unavailable;
    var asset = await catalog.GetAsync(new AssetId(assetId), cancellationToken);
    return asset is null ? Results.NotFound() : Results.Ok(asset);
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapPost("/catalog/assets", async (
    CreateAssetRequest request,
    ClaimsPrincipal principal,
    IAssetCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    var result = await catalog.CreateAsync(request.Title, actorId, cancellationToken);
    return result.Status switch
    {
        CatalogMutationStatus.Created => Results.Created($"{configuredApiBasePath}/v1/catalog/assets/{result.Asset!.Id:D}", result.Asset),
        CatalogMutationStatus.Invalid => Results.BadRequest(new { error = "invalid_asset", detail = result.Error }),
        CatalogMutationStatus.Unavailable => Results.Json(new { error = "catalog_unavailable", detail = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Problem("Unexpected catalog result.")
    };
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapPatch("/catalog/assets/{assetId:guid}/title", async (
    Guid assetId,
    UpdateAssetTitleRequest request,
    ClaimsPrincipal principal,
    IAssetCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    var result = await catalog.UpdateTitleAsync(new AssetId(assetId), request.Title, request.ExpectedVersion, actorId, cancellationToken);
    return result.Status switch
    {
        CatalogMutationStatus.Updated => Results.Ok(result.Asset),
        CatalogMutationStatus.NotFound => Results.NotFound(),
        CatalogMutationStatus.Conflict => Results.Conflict(new { error = "concurrency_conflict", detail = result.Error, current = result.Asset }),
        CatalogMutationStatus.Invalid => Results.BadRequest(new { error = "invalid_asset", detail = result.Error }),
        CatalogMutationStatus.Unavailable => Results.Json(new { error = "catalog_unavailable", detail = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Problem("Unexpected catalog result.")
    };
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapPost("/uploads/sessions", async (
    CreateUploadSessionRequest request,
    ClaimsPrincipal principal,
    IDurableUploadService uploads,
    CancellationToken cancellationToken) =>
{
    try
    {
        var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var session = await uploads.CreateSessionAsync(request, actorId, cancellationToken);
        return Results.Created($"{configuredApiBasePath}/v1/uploads/sessions/{session.Session.SessionId:D}", session);
    }
    catch (UploadRequestException ex) { return UploadFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapGet("/uploads/sessions/{sessionId:guid}", async (Guid sessionId, IDurableUploadService uploads, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await uploads.GetSessionAsync(sessionId, cancellationToken)); }
    catch (UploadRequestException ex) { return UploadFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapPut("/uploads/sessions/{sessionId:guid}/chunks", async (
    Guid sessionId, long offset, HttpRequest request, ClaimsPrincipal principal, IDurableUploadService uploads, CancellationToken cancellationToken) =>
{
    try
    {
        var chunkSha = request.Headers["X-Chunk-SHA256"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(chunkSha)) return Results.BadRequest(new { error = "chunk_sha256_required", detail = "X-Chunk-SHA256 header is required." });
        var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        return Results.Ok(await uploads.PutChunkAsync(sessionId, offset, chunkSha, request.Body, actorId, cancellationToken));
    }
    catch (UploadRequestException ex) { return UploadFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapPost("/uploads/sessions/{sessionId:guid}/finalize", async (
    Guid sessionId, ClaimsPrincipal principal, IDurableUploadService uploads, CancellationToken cancellationToken) =>
{
    try
    {
        var actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        return Results.Ok(await uploads.FinalizeAsync(sessionId, actorId, cancellationToken));
    }
    catch (UploadRequestException ex) { return UploadFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapGet("/processing/profiles", (IMediaProcessingService processing) => Results.Ok(processing.Profiles))
    .RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/processing/jobs", async (int? limit, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await processing.ListJobsAsync(limit ?? 100, cancellationToken)); }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapPost("/processing/assets/{assetId:guid}/jobs", async (
    Guid assetId, EnqueueProcessingRequest request, ClaimsPrincipal principal, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try
    {
        var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var job = await processing.EnqueueAsync(assetId, request.ProfileId, actor, cancellationToken);
        return Results.Ok(job);
    }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapPost("/processing/jobs/{jobId:guid}/retry", async (
    Guid jobId, ClaimsPrincipal principal, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try
    {
        var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        return Results.Ok(await processing.RetryAsync(jobId, actor, cancellationToken));
    }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogWritePolicy);

api.MapGet("/processing/assets/{assetId:guid}/technical", async (Guid assetId, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try
    {
        var technical = await processing.GetTechnicalMetadataAsync(assetId, cancellationToken);
        return technical is null ? Results.NotFound() : Results.Ok(technical);
    }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/processing/assets/{assetId:guid}/derivatives", async (Guid assetId, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await processing.ListDerivativesAsync(assetId, cancellationToken)); }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/processing/assets/{assetId:guid}/derivatives/{derivativeId:guid}/content", async (
    Guid assetId, Guid derivativeId, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try
    {
        var payload = await processing.OpenDerivativeAsync(assetId, derivativeId, cancellationToken);
        return payload is null
            ? Results.NotFound()
            : Results.Stream(payload.Content, payload.ContentType, fileDownloadName: payload.FileName, enableRangeProcessing: true);
    }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/processing/assets/{assetId:guid}/preview/original", async (Guid assetId, IMediaProcessingService processing, CancellationToken cancellationToken) =>
{
    try
    {
        var payload = await processing.OpenOriginalPreviewAsync(assetId, cancellationToken);
        return payload is null
            ? Results.NotFound()
            : Results.Stream(payload.Content, payload.ContentType, fileDownloadName: payload.FileName, enableRangeProcessing: true);
    }
    catch (ProcessingRequestException ex) { return ProcessingFailure(ex); }
}).RequireAuthorization(MamSecurity.CatalogReadPolicy);

api.MapGet("/audit/recent", async (int? limit, IAuditSink audit, CancellationToken cancellationToken) =>
{
    var events = await audit.ListRecentAsync(limit ?? 50, cancellationToken);
    return Results.Ok(events);
}).RequireAuthorization(MamSecurity.AuditReadPolicy);

app.Run();

static async ValueTask<IResult?> CatalogUnavailableAsync(IAssetCatalog catalog, CancellationToken cancellationToken)
{
    var health = await catalog.GetHealthAsync(cancellationToken);
    return health.IsReady ? null : Results.Json(new { error = "catalog_unavailable", detail = health.Detail }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

static IResult UploadFailure(UploadRequestException ex) =>
    Results.Json(new { error = ex.Code, detail = ex.Message, existingAssetId = ex.ExistingAssetId }, statusCode: ex.StatusCode);
static IResult ProcessingFailure(ProcessingRequestException ex) =>
    Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);

internal sealed record CreateAssetRequest(string Title);
internal sealed record UpdateAssetTitleRequest(string Title, long ExpectedVersion);
internal sealed record MetadataValidationRequest(IReadOnlyDictionary<string, string?>? Values);
internal sealed record EnqueueProcessingRequest(string ProfileId);
