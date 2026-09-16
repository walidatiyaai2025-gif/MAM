using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.Identity;
using MAM.Application.MediaLibrary;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Discovery;

namespace MAM.Api;

public static class P133MediaLibraryEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var api = app.MapGroup($"{configuredApiBasePath}/v1/media-library");

        api.MapGet("/snapshot", async (
            ClaimsPrincipal principal,
            IServiceProvider services,
            IDiscoveryService discovery,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var store = ResolveStore(services);
                var rows = await store.ListAsync(cancellationToken);
                var roles = Roles(principal);
                var capabilityCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var visible = new List<MediaLibraryAssetSnapshot>(rows.Count);
                foreach (var row in rows)
                {
                    if (!capabilityCache.TryGetValue(row.MediaKind, out var allowed))
                    {
                        allowed = await discovery.IsMediaActionAllowedAsync(roles, row.MediaKind, "view", cancellationToken);
                        capabilityCache[row.MediaKind] = allowed;
                    }
                    if (allowed) visible.Add(row);
                }

                var categories = await discovery.ListCategoriesAsync(cancellationToken);
                return Results.Ok(new MediaLibrarySnapshot(categories, visible));
            }
            catch (MediaLibraryRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/assets/{assetId:guid}", async (
            Guid assetId,
            ClaimsPrincipal principal,
            IServiceProvider services,
            IDiscoveryService discovery,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "view", cancellationToken)) return Results.Forbid();
                return Results.Ok(await ResolveStore(services).GetAsync(assetId, cancellationToken));
            }
            catch (MediaLibraryRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPut("/assets/{assetId:guid}/organization", async (
            Guid assetId,
            UpdateMediaOrganizationRequest request,
            ClaimsPrincipal principal,
            IServiceProvider services,
            IDiscoveryService discovery,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!await Allowed(discovery, principal, assetId, "edit", cancellationToken)) return Results.Forbid();
                var updated = await ResolveStore(services).UpdateAsync(assetId, request, Actor(principal), cancellationToken);
                return Results.Ok(updated);
            }
            catch (MediaLibraryRequestException ex) { return Failure(ex); }
            catch (DiscoveryRequestException ex) { return DiscoveryFailure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);
    }

    private static MediaLibraryOrganizationStore ResolveStore(IServiceProvider services) =>
        new(
            services.GetService<SqlServerConnectionFactory>(),
            services.GetService<DemoSqliteDatabase>(),
            services.GetRequiredService<IAuditSink>());

    private static async Task<bool> Allowed(
        IDiscoveryService discovery,
        ClaimsPrincipal principal,
        Guid assetId,
        string action,
        CancellationToken cancellationToken)
    {
        var mediaKind = await discovery.GetAssetMediaKindAsync(assetId, cancellationToken);
        return await discovery.IsMediaActionAllowedAsync(Roles(principal), mediaKind, action, cancellationToken);
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";

    private static string[] Roles(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IResult Failure(MediaLibraryRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message, current = ex.Current }, statusCode: ex.StatusCode);

    private static IResult DiscoveryFailure(DiscoveryRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
}
