using MAM.Application.Administration;
using MAM.Application.Catalog;
using MAM.Application.Curation;
using MAM.Application.Diagnostics;
using MAM.Application.Identity;
using MAM.Application.Operations;
using MAM.Application.Processing;
using MAM.Application.Protection;
using MAM.Application.Uploads;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Operations;

namespace MAM.Api;

public static class P09OperationsBootstrap
{
    public const string CorrelationHeader = "X-Correlation-ID";

    public static void Add(IServiceCollection services, bool sqlConfigured, MamSettings settings)
    {
        if (sqlConfigured)
        {
            services.AddSingleton<IOperationsService>(sp => new SqlServerOperationsService(
                sp.GetRequiredService<SqlServerConnectionFactory>(),
                settings.Storage.Primary.Id,
                settings.Storage.Backup.Id));
        }
        else
        {
            services.AddSingleton<IOperationsService>(_ => new UnavailableOperationsService(
                "Authoritative P09 reports require the SQL Server operational state store. Missing SQL authority is fail-closed."));
        }
    }

    public static void UseCorrelation(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers[CorrelationHeader].FirstOrDefault();
            var correlationId = IsSafeCorrelationId(supplied) ? supplied! : Guid.NewGuid().ToString("N");
            context.TraceIdentifier = correlationId;
            context.Response.Headers[CorrelationHeader] = correlationId;
            using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next(context);
            }
        });
    }

    private static bool IsSafeCorrelationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':');
}

public static class P09OperationsEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath, MamSettings settings)
    {
        app.MapGet("/health/operations", async (IOperationsService operations, CancellationToken cancellationToken) =>
        {
            var health = await operations.GetHealthAsync(cancellationToken);
            return health.IsReady
                ? Results.Ok(new { status = "Ready", operations = health })
                : Results.Json(new { status = "Degraded", operations = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        var operationsApi = app.MapGroup($"{configuredApiBasePath}/v1/operations");

        operationsApi.MapGet("/summary", async (IOperationsService operations, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await operations.GetSummaryAsync(cancellationToken)); }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/throughput", async (int? windowHours, IOperationsService operations, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await operations.GetIngestThroughputAsync(windowHours ?? 24, cancellationToken)); }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/queues", async (IOperationsService operations, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await operations.GetQueuesAsync(cancellationToken)); }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/integrity", async (IOperationsService operations, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await operations.GetIntegrityAsync(cancellationToken)); }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/storage", async (IOperationsService operations, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await operations.GetStorageUsageAsync(cancellationToken)); }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/dependencies", async (
            IOperationsService operations,
            IAssetCatalog catalog,
            IDurableUploadService uploads,
            IMediaProcessingService processing,
            ICurationService curation,
            IBackupProtectionService protection,
            IAdministrationService administration,
            CancellationToken cancellationToken) =>
        {
            var result = await BuildDependenciesAsync(operations, catalog, uploads, processing, curation, protection, administration, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        operationsApi.MapGet("/diagnostics", async (
            HttpContext context,
            IOperationsService operations,
            IAssetCatalog catalog,
            IDurableUploadService uploads,
            IMediaProcessingService processing,
            ICurationService curation,
            IBackupProtectionService protection,
            IAdministrationService administration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var build = BuildInfo.Current.WithEnvironment(settings.Environment.Name);
                var summary = await operations.GetSummaryAsync(cancellationToken);
                var queues = await operations.GetQueuesAsync(cancellationToken);
                var integrity = await operations.GetIntegrityAsync(cancellationToken);
                var dependencies = await BuildDependenciesAsync(operations, catalog, uploads, processing, curation, protection, administration, cancellationToken);
                var bundle = new DiagnosticsBundle(
                    settings.Brand.ProductNameEn,
                    "P09",
                    settings.Environment.Name,
                    settings.Environment.SiteCode,
                    build.Version,
                    build.CommitSha,
                    context.TraceIdentifier,
                    summary,
                    queues,
                    integrity,
                    dependencies,
                    [
                        "No database connection strings or resolved secret values are included.",
                        "No storage filesystem roots or credential references are included.",
                        "Failure details are normalized to dependency state and never echo exception messages."
                    ],
                    DateTimeOffset.UtcNow);
                return Results.Ok(bundle);
            }
            catch (OperationsRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private static async Task<DependencyHealthReport> BuildDependenciesAsync(
        IOperationsService operations,
        IAssetCatalog catalog,
        IDurableUploadService uploads,
        IMediaProcessingService processing,
        ICurationService curation,
        IBackupProtectionService protection,
        IAdministrationService administration,
        CancellationToken cancellationToken)
    {
        var op = await operations.GetHealthAsync(cancellationToken);
        var cat = await catalog.GetHealthAsync(cancellationToken);
        var upload = await uploads.GetHealthAsync(cancellationToken);
        var proc = await processing.GetHealthAsync(cancellationToken);
        var cur = await curation.GetHealthAsync(cancellationToken);
        var backup = await protection.GetHealthAsync(cancellationToken);
        var admin = await administration.GetHealthAsync(cancellationToken);
        var items = new List<DependencyHealthItem>
        {
            Safe("Operations SQL", op.IsReady, op.Provider),
            Safe("Catalog SQL", cat.IsReady, cat.Provider),
            Safe("Primary upload", upload.IsReady, upload.Provider, upload.TargetId),
            Safe("Media processing", proc.IsReady, proc.Provider),
            Safe("Search/curation", cur.IsReady, cur.Provider),
            Safe("Backup protection", backup.IsReady, "ServerManaged", backup.BackupTargetId),
            Safe("Administration", admin.IsReady, admin.Provider)
        };
        return new DependencyHealthReport(items, DateTimeOffset.UtcNow);
    }

    private static DependencyHealthItem Safe(string dependency, bool ready, string provider, string? targetId = null) =>
        new(dependency, ready ? "Ready" : "Degraded", ready ? $"{provider} dependency is ready." : $"{provider} dependency is degraded.", targetId);

    private static IResult Failure(OperationsRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
}
