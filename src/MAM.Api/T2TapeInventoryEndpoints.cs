using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Application.Tapes;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Tapes;

namespace MAM.Api;

public static class T2TapeInventoryEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var api = app.MapGroup($"{configuredApiBasePath}/v1/tapes");

        api.MapGet("/", async (string? query, int? limit, IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Resolve(services).ListAsync(query, limit ?? 100, ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/{tapeId:guid}", async (Guid tapeId, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                var item = await Resolve(services).GetAsync(tapeId, ct);
                return item is null ? Results.NotFound(new { error = "tape_not_found" }) : Results.Ok(item);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/resolve/{tapeCode}", async (string tapeCode, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                var item = await Resolve(services).ResolveCodeAsync(tapeCode, ct);
                return item is null ? Results.NotFound(new { error = "tape_not_found" }) : Results.Ok(item);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/", async (CreateTapeRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                var created = await Resolve(services).CreateAsync(request, Actor(principal), ct);
                return Results.Created($"{configuredApiBasePath}/v1/tapes/{created.TapeId:D}", created);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPut("/{tapeId:guid}", async (Guid tapeId, UpdateTapeRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Resolve(services).UpdateAsync(tapeId, request, Actor(principal), ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/formats/list", async (bool? includeInactive, IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Resolve(services).ListFormatsAsync(includeInactive == true, ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPut("/formats/{code}", async (string code, UpsertTapeFormatRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            if (!string.Equals(code, request.Code, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "format_code_mismatch", detail = "Route and body format codes must match." });
            try { return Results.Ok(await Resolve(services).UpsertFormatAsync(request, Actor(principal), ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private static ITapeInventoryService Resolve(IServiceProvider services)
    {
        var sql = services.GetService<SqlServerConnectionFactory>();
        var demo = services.GetService<DemoSqliteDatabase>();
        if (sql is null && demo is null)
            throw new TapeInventoryRequestException("tape_inventory_unavailable", "Authoritative tape inventory storage is not configured.", StatusCodes.Status503ServiceUnavailable);
        return new TapeInventoryStore(sql, demo, services.GetRequiredService<IAuditSink>());
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";

    private static IResult Failure(TapeInventoryRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message, current = ex.Current }, statusCode: ex.StatusCode);
}
