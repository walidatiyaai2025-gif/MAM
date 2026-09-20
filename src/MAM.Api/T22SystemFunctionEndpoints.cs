using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Application.SystemFunctions;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.SystemFunctions;

namespace MAM.Api;

public static class T22SystemFunctionEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var group = app.MapGroup($"{configuredApiBasePath}/v1/system-functions");

        group.MapGet("/effective", async (IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                var rows = await Resolve(services).ListAsync(ct);
                return Results.Ok(rows.Select(x => new { x.FunctionKey, x.IsEnabled }).ToArray());
            }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        group.MapGet("/", async (IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Resolve(services).ListAsync(ct)); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.SystemFunctionsViewPolicy);

        group.MapPut("/{functionKey}", async (
            string functionKey,
            UpdateSystemFunctionRequest request,
            ClaimsPrincipal principal,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await Resolve(services).UpdateAsync(functionKey,request,Actor(principal),ct));
            }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.SystemFunctionsManagePolicy);
    }

    public static ISystemFunctionService Resolve(IServiceProvider services)
    {
        var sql = services.GetService<SqlServerConnectionFactory>();
        var demo = services.GetService<DemoSqliteDatabase>();
        if (sql is null && demo is null)
            throw new SystemFunctionRequestException("system_functions_unavailable","System functions require an authoritative SQL Server or Demo SQLite store.",503);
        return new SystemFunctionStore(sql,demo,services.GetRequiredService<IAuditSink>());
    }

    public static async Task<bool> EnabledAsync(IServiceProvider services, string key, CancellationToken ct) =>
        await Resolve(services).IsEnabledAsync(key,ct);

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.WindowsAccountName)
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.Identity?.Name
        ?? "unknown";

    private static IResult Failure(SystemFunctionRequestException ex) =>
        Results.Json(new { error=ex.Code,detail=ex.Message,current=ex.Current },statusCode:ex.StatusCode);
}
