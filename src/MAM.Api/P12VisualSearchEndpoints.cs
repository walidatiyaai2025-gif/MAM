using System.Security.Claims;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Discovery;

namespace MAM.Api;

public static class P12VisualSearchEndpoints
{
    private const long MaxQueryImageBytes = 16L * 1024 * 1024;

    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        app.MapGet("/health/visual-search", async (IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var visual = Resolve(services);
            if (visual is null)
                return Results.Json(new { status = "Degraded", detail = "Visual search authority is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            var health = await visual.GetHealthAsync(cancellationToken);
            return health.IsReady ? Results.Ok(new { status = "Ready", visual = health }) : Results.Json(new { status = "Degraded", visual = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        var api = app.MapGroup($"{configuredApiBasePath}/v1/discovery");

        api.MapGet("/assets/{assetId:guid}/visual-segments", async (
            Guid assetId,
            string? sourceKind,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await AllowedAsync(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                var visual = ResolveRequired(services);
                return Results.Ok(await visual.ListSegmentsAsync(assetId, string.IsNullOrWhiteSpace(sourceKind) ? DiscoverySources.Transcript : sourceKind, cancellationToken));
            }
            catch (VisualSearchRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/assets/{assetId:guid}/visual-segments/{segmentId:guid}/thumbnail", async (
            Guid assetId,
            Guid segmentId,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await AllowedAsync(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                var payload = await ResolveRequired(services).OpenThumbnailAsync(assetId, segmentId, cancellationToken);
                return payload is null ? Results.NotFound() : Results.Stream(payload.Content, payload.ContentType, fileDownloadName: payload.FileName, enableRangeProcessing: false);
            }
            catch (VisualSearchRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/image-search", async (
            HttpRequest request,
            int? limit,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (request.ContentLength is null or <= 0) return Results.BadRequest(new { error = "image_required", detail = "Upload an image to search visually similar media." });
                if (request.ContentLength > MaxQueryImageBytes) return Results.Json(new { error = "image_too_large", detail = "Visual search images are limited to 16 MB." }, statusCode: StatusCodes.Status413PayloadTooLarge);
                var contentType = request.ContentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
                if (contentType is not ("image/jpeg" or "image/png" or "image/bmp" or "image/gif" or "image/tiff" or "image/webp"))
                    return Results.Json(new { error = "image_type_not_supported", detail = "Upload a supported image file (JPEG, PNG, BMP, GIF, TIFF or WebP)." }, statusCode: StatusCodes.Status415UnsupportedMediaType);

                var visual = ResolveRequired(services);
                var result = await visual.SearchAsync(request.Body, null, contentType, limit ?? 30, cancellationToken);
                var roles = Roles(principal);
                var visible = new List<VisualSearchHit>();
                foreach (var item in result.Items)
                    if (await discovery.IsMediaActionAllowedAsync(roles, item.MediaKind, "view", cancellationToken)) visible.Add(item);
                return Results.Ok(result with { Items = visible });
            }
            catch (VisualSearchRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/assets/{assetId:guid}/visual-reindex", async (
            Guid assetId,
            ClaimsPrincipal principal,
            IDiscoveryService discovery,
            IMediaProcessingService processing,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await AllowedAsync(discovery, principal, assetId, "process", cancellationToken)) return Results.Forbid();
                var kind = await discovery.GetAssetMediaKindAsync(assetId, cancellationToken);
                var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                if (services.GetService<DemoSqliteDatabase>() is not null)
                {
                    if (!string.Equals(kind, MediaKinds.Image, StringComparison.OrdinalIgnoreCase))
                        return Results.Json(new { error = "demo_visual_reindex_type_not_supported", detail = "Offline Demo can reindex image originals locally; video transcript frame generation remains a production Worker operation." }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                    await ResolveRequired(services).IndexAssetAsync(assetId, cancellationToken);
                    return Results.Ok(new { state = "Succeeded", profileId = BuiltInProcessingProfiles.VisualIndex, assetId });
                }

                var profile = string.Equals(kind, MediaKinds.Image, StringComparison.OrdinalIgnoreCase)
                    ? BuiltInProcessingProfiles.VisualIndex
                    : string.Equals(kind, MediaKinds.Video, StringComparison.OrdinalIgnoreCase) || string.Equals(kind, MediaKinds.Audio, StringComparison.OrdinalIgnoreCase)
                        ? BuiltInProcessingProfiles.VisualSegments
                        : null;
                if (profile is null)
                    return Results.Json(new { error = "visual_reindex_type_not_supported", detail = "Visual reindex currently supports image originals and video/audio transcript segments." }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                if (profile == BuiltInProcessingProfiles.VisualSegments && await discovery.GetTextAsync(assetId, DiscoverySources.Transcript, cancellationToken) is null)
                    return Results.Conflict(new { error = "transcript_required", detail = "Create the timestamped transcript before visual segment indexing." });
                var job = await processing.EnqueueAsync(assetId, profile, actor, cancellationToken);
                if (job.State == ProcessingJobState.Failed) job = await processing.RetryAsync(job.JobId, actor, cancellationToken);
                return Results.Ok(job);
            }
            catch (VisualSearchRequestException ex) { return Failure(ex); }
            catch (ProcessingRequestException ex) { return Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);
    }

    private static IVisualSearchService? Resolve(IServiceProvider services)
    {
        var storage = services.GetService<IStorageObjectStore>();
        var settings = services.GetService<MamSettings>();
        if (storage is null || settings is null) return null;
        var provider = new LocalImageVisualEmbeddingProvider();
        if (services.GetService<SqlServerConnectionFactory>() is { } sql)
            return new SqlServerVisualSearchService(sql, storage, provider, settings);
        if (services.GetService<DemoSqliteDatabase>() is { } demo)
            return new DemoVisualSearchService(demo, storage, provider, settings);
        return null;
    }

    private static IVisualSearchService ResolveRequired(IServiceProvider services) =>
        Resolve(services) ?? throw new VisualSearchRequestException("visual_search_unavailable", "Visual search authority is unavailable.", 503);

    private static async Task<bool> AllowedAsync(IDiscoveryService discovery, ClaimsPrincipal principal, Guid assetId, string action, CancellationToken cancellationToken)
    {
        var kind = await discovery.GetAssetMediaKindAsync(assetId, cancellationToken);
        return await discovery.IsMediaActionAllowedAsync(Roles(principal), kind, action, cancellationToken);
    }

    private static string[] Roles(ClaimsPrincipal principal) => principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IResult Failure(VisualSearchRequestException ex) => Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
    private static IResult DiscoveryFailure(DiscoveryRequestException ex) => Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
}