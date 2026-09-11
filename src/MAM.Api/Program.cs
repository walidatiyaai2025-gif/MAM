using System.Security.Claims;
using MAM.Api.Security;
using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Diagnostics;
using MAM.Application.Identity;
using MAM.Application.Metadata;
using MAM.Domain.Assets;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Secrets;
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
}
else if (string.Equals(mamSettings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAuditSink, InMemoryAuditSink>();
    builder.Services.AddSingleton<IAssetCatalog, DevelopmentAssetCatalog>();
}
else
{
    builder.Services.AddSingleton<IAuditSink, InMemoryAuditSink>();
    builder.Services.AddSingleton<IAssetCatalog>(_ => new UnavailableAssetCatalog(
        "Authoritative SQL Server catalog secret could not be resolved. Non-development catalog access is fail-closed."));
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
    phase = "P02",
    environment = mamSettings.Environment.Name,
    catalogProvider = sqlConfigured ? "SqlServer" : string.Equals(mamSettings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase) ? "DevelopmentMemory" : "Unavailable"
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

api.MapGet("/audit/recent", async (int? limit, IAuditSink audit, CancellationToken cancellationToken) =>
{
    var events = await audit.ListRecentAsync(limit ?? 50, cancellationToken);
    return Results.Ok(events);
}).RequireAuthorization(MamSecurity.AuditReadPolicy);

app.Run();

static async ValueTask<IResult?> CatalogUnavailableAsync(IAssetCatalog catalog, CancellationToken cancellationToken)
{
    var health = await catalog.GetHealthAsync(cancellationToken);
    return health.IsReady
        ? null
        : Results.Json(new { error = "catalog_unavailable", detail = health.Detail }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

internal sealed record CreateAssetRequest(string Title);
internal sealed record UpdateAssetTitleRequest(string Title, long ExpectedVersion);
internal sealed record MetadataValidationRequest(IReadOnlyDictionary<string, string?>? Values);
