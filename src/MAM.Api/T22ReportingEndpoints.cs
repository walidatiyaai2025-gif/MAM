using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Discovery;
using MAM.Application.Identity;
using MAM.Application.SystemFunctions;

namespace MAM.Api;

public static class T22ReportingEndpoints
{
    public static void Map(WebApplication app,string configuredApiBasePath)
    {
        var reports=app.MapGroup($"{configuredApiBasePath}/v1/reports");

        reports.MapGet("/full-content",async (
            ClaimsPrincipal principal,
            IAssetCatalog catalog,
            IDiscoveryService discovery,
            IAuditSink audit,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if(!await T22SystemFunctionEndpoints.EnabledAsync(services,MamSystemFunctionKeys.PrintableReports,ct))
                return Results.Json(new{error="system_function_disabled",functionKey=MamSystemFunctionKeys.PrintableReports},statusCode:409);

            var assets=(await catalog.ListAsync(ct))
                .Where(x=>!string.Equals(x.Lifecycle,"Deleted",StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var gate=new SemaphoreSlim(8);
            var kinds=await Task.WhenAll(assets.Select(async asset =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    try { return await discovery.GetAssetMediaKindAsync(asset.Id,ct); }
                    catch { return "Other"; }
                }
                finally { gate.Release(); }
            }));

            var known=new[]{"Video","Audio","Image","Document","Other"};
            var counts=known.ToDictionary(x=>x,x=>0,StringComparer.OrdinalIgnoreCase);
            foreach(var raw in kinds)
            {
                var kind=known.FirstOrDefault(x=>string.Equals(x,raw,StringComparison.OrdinalIgnoreCase))??"Other";
                counts[kind]++;
            }

            var generatedAt=DateTimeOffset.UtcNow;
            var actor=Actor(principal);
            await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),generatedAt,actor,"report.full-content.generated","Report","full-content","Success",$"total={assets.Length}"),ct);

            return Results.Ok(new
            {
                reportKey="full-content",
                titleEn="Full Content Report",
                titleAr="تقرير المحتوى بالكامل",
                generatedAtUtc=generatedAt,
                generatedBy=principal.Identity?.Name??actor,
                totalFiles=assets.Length,
                byMediaType=known.Select(x=>new{mediaType=x,fileCount=counts[x]}).ToArray()
            });
        }).RequireAuthorization(MamSecurity.AuditReadPolicy);
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.WindowsAccountName)
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.Identity?.Name
        ?? "unknown";
}
