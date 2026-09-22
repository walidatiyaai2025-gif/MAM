using System.Security.Claims;
using MAM.Application.Discovery;
using MAM.Application.Identity;

namespace MAM.Api;

public static class P12DiscoveryEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        P133MediaLibraryEndpoints.Map(app, configuredApiBasePath);
        var api = app.MapGroup($"{configuredApiBasePath}/v1/discovery");

        api.MapGet("/health", async (IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            var health = await discovery.GetHealthAsync(cancellationToken);
            return health.IsReady ? Results.Ok(health) : Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/dashboard", async (IDiscoveryService discovery, CancellationToken cancellationToken) =>
            Results.Ok(await discovery.GetDashboardAsync(cancellationToken)))
            .RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/my-media-capabilities", async (ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            var roleSet = Roles(principal).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = await discovery.ListMediaPermissionsAsync(cancellationToken);
            var result = new List<MediaCapabilitySnapshot>();
            foreach (var kind in new[] { MediaKinds.Video, MediaKinds.Audio, MediaKinds.Image, MediaKinds.Document, MediaKinds.Other })
            {
                var relevant = rows.Where(row => roleSet.Contains(row.RoleName) && string.Equals(row.MediaKind, kind, StringComparison.OrdinalIgnoreCase)).ToArray();
                result.Add(new MediaCapabilitySnapshot(
                    kind,
                    relevant.Any(row => row.CanView),
                    relevant.Any(row => row.CanUpload),
                    relevant.Any(row => row.CanEdit),
                    relevant.Any(row => row.CanProcess),
                    relevant.Any(row => row.CanDownload)));
            }
            return Results.Ok(result);
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/categories", async (IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                var categories = await discovery.ListCategoriesAsync(cancellationToken);
                return Results.Ok(categories ?? Array.Empty<CategorySnapshot>());
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(
                    new { error = "discovery_categories_unavailable", detail = "The category service is temporarily unavailable. Retry the request." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/categories", async (CreateCategoryRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await discovery.CreateCategoryAsync(request, Actor(principal), cancellationToken)); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPut("/categories/{categoryId:guid}", async (Guid categoryId, UpdateCategoryRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await discovery.UpdateCategoryAsync(categoryId, request, Actor(principal), cancellationToken)); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapDelete("/categories/{categoryId:guid}", async (Guid categoryId, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { await discovery.DeleteCategoryAsync(categoryId, Actor(principal), cancellationToken); return Results.NoContent(); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/assets/{assetId:guid}/category", async (Guid assetId, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.GetAssetCategoryAsync(assetId, cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPut("/assets/{assetId:guid}/category", async (Guid assetId, AssignAssetCategoryRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "edit", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.AssignAssetCategoryAsync(assetId, request.CategoryId, Actor(principal), cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/assets/{assetId:guid}/text/{sourceKind}", async (Guid assetId, string sourceKind, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                var text = await discovery.GetTextAsync(assetId, sourceKind, cancellationToken);
                return text is null ? Results.NotFound() : Results.Ok(text);
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/assets/{assetId:guid}/extraction-status", async (Guid assetId, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.GetExtractionStatusAsync(assetId, cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/search", async (string? query, int? page, int? pageSize, Guid? categoryId, string? mediaKind, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await discovery.SearchAsync(new DiscoverySearchRequest(query ?? string.Empty, page ?? 1, pageSize ?? 50, categoryId, mediaKind), cancellationToken);
                var visible = new List<DiscoverySearchHit>();
                foreach (var item in result.Items)
                    if (await discovery.IsMediaActionAllowedAsync(Roles(principal), item.MediaKind, "view", cancellationToken)) visible.Add(item);
                return Results.Ok(result with { Items = visible, TotalCount = visible.Count == result.Items.Count ? result.TotalCount : visible.Count });
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/references", async (IDiscoveryService discovery, CancellationToken cancellationToken) =>
            Results.Ok(await discovery.ListReferenceSubjectsAsync(cancellationToken)))
            .RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/references", async (CreateReferenceSubjectRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await discovery.CreateReferenceSubjectAsync(request, Actor(principal), cancellationToken)); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPut("/references/{subjectId:guid}", async (Guid subjectId, UpdateReferenceSubjectRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await discovery.UpdateReferenceSubjectAsync(subjectId, request, Actor(principal), cancellationToken)); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapDelete("/references/{subjectId:guid}", async (Guid subjectId, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                await discovery.DeleteReferenceSubjectAsync(subjectId, Actor(principal), cancellationToken);
                return Results.NoContent();
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPost("/references/{subjectId:guid}/images", async (Guid subjectId, AddReferenceImageRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, request.AssetId, "edit", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.AddReferenceImageAsync(subjectId, request.AssetId, Actor(principal), cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/assets/{assetId:guid}/reference-tags", async (Guid assetId, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.ListAssetReferenceTagsAsync(assetId, cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/assets/{assetId:guid}/reference-tags", async (Guid assetId, AddAssetReferenceTagRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "edit", cancellationToken)) return Results.Forbid();
                return Results.Ok(await discovery.AddAssetReferenceTagAsync(assetId, request, Actor(principal), cancellationToken));
            }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/media-permissions", async (IDiscoveryService discovery, CancellationToken cancellationToken) =>
            Results.Ok(await discovery.ListMediaPermissionsAsync(cancellationToken)))
            .RequireAuthorization(MamSecurity.AdministrationPolicy);

        api.MapPut("/media-permissions", async (UpsertMediaPermissionRequest request, ClaimsPrincipal principal, IDiscoveryService discovery, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await discovery.UpsertMediaPermissionAsync(request, Actor(principal), cancellationToken)); }
            catch (DiscoveryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private static async Task<bool> Allowed(IDiscoveryService discovery, ClaimsPrincipal principal, Guid assetId, string action, CancellationToken cancellationToken)
    {
        var mediaKind = await discovery.GetAssetMediaKindAsync(assetId, cancellationToken);
        return await discovery.IsMediaActionAllowedAsync(Roles(principal), mediaKind, action, cancellationToken);
    }

    private static string Actor(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";
    private static string[] Roles(ClaimsPrincipal principal) => principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IResult Failure(DiscoveryRequestException ex) => Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
}
