using System.Security.Claims;
using MAM.Application.Identity;
using MAM.Application.TapeInventory;
using MAM.Infrastructure.TapeInventory;

namespace MAM.Api;

public static class T21TapeInventoryBootstrap
{
    public static void Add(IServiceCollection services, bool sqlConfigured)
    {
        if (sqlConfigured)
            services.AddSingleton<ITapeInventoryService, SqlServerTapeInventoryService>();
        else
            services.AddSingleton<ITapeInventoryService>(_ => new UnavailableTapeInventoryService(
                "Authoritative tape inventory requires the central SQL Server store."));
    }
}

public static class T21TapeInventoryEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        if (app.Services.GetService<ITapeInventoryService>() is null) return;

        app.MapGet("/health/tape-inventory", async (ITapeInventoryService service, CancellationToken cancellationToken) =>
        {
            var health = await service.GetHealthAsync(cancellationToken);
            return health.IsReady
                ? Results.Ok(new { status = "Ready", tapeInventory = health })
                : Results.Json(new { status = "Degraded", tapeInventory = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        var tapes = app.MapGroup($"{configuredApiBasePath}/v1/tapes");

        tapes.MapGet("/", async (string? query, ITapeInventoryService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListAsync(query, cancellationToken)); }
            catch (InvalidOperationException ex) { return Unavailable(ex.Message); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = "invalid_query", detail = ex.Message }); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        tapes.MapGet("/{tapeId:guid}", async (Guid tapeId, ITapeInventoryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var tape = await service.GetAsync(tapeId, cancellationToken);
                return tape is null ? Results.NotFound() : Results.Ok(tape);
            }
            catch (InvalidOperationException ex) { return Unavailable(ex.Message); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        tapes.MapGet("/by-code/{tapeCode}", async (string tapeCode, ITapeInventoryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var tape = await service.GetByCodeAsync(tapeCode, cancellationToken);
                return tape is null ? Results.NotFound() : Results.Ok(tape);
            }
            catch (InvalidOperationException ex) { return Unavailable(ex.Message); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = "invalid_tape_code", detail = ex.Message }); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        tapes.MapPost("/", async (
            CreateTapeRequest request,
            ClaimsPrincipal principal,
            ITapeInventoryService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, Actor(principal), cancellationToken);
            return result.Status switch
            {
                TapeMutationStatus.Created => Results.Created($"{configuredApiBasePath}/v1/tapes/{result.Tape!.TapeId:D}", result.Tape),
                TapeMutationStatus.Invalid => Results.BadRequest(new { error = "invalid_tape", detail = result.Error }),
                TapeMutationStatus.Unavailable => Unavailable(result.Error),
                _ => Results.Problem("Unexpected tape creation result.")
            };
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        tapes.MapPut("/{tapeId:guid}", async (
            Guid tapeId,
            UpdateTapeRequest request,
            ClaimsPrincipal principal,
            ITapeInventoryService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(tapeId, request, Actor(principal), cancellationToken);
            return result.Status switch
            {
                TapeMutationStatus.Updated => Results.Ok(result.Tape),
                TapeMutationStatus.NotFound => Results.NotFound(),
                TapeMutationStatus.Conflict => Results.Conflict(new { error = "concurrency_conflict", detail = result.Error, current = result.Tape }),
                TapeMutationStatus.Invalid => Results.BadRequest(new { error = "invalid_tape", detail = result.Error }),
                TapeMutationStatus.Unavailable => Unavailable(result.Error),
                _ => Results.Problem("Unexpected tape update result.")
            };
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);
    }

    private static string Actor(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    private static IResult Unavailable(string? detail) => Results.Json(
        new { error = "tape_inventory_unavailable", detail = detail ?? "Tape inventory is unavailable." },
        statusCode: StatusCodes.Status503ServiceUnavailable);
}
