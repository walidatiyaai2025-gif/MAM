using System.Security.Claims;
using System.Text;
using MAM.Api.Security;
using MAM.Application.BulkImport;
using MAM.Application.Identity;

namespace MAM.Api;

public static class BulkImportEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var api = app.MapGroup($"{configuredApiBasePath}/v1/bulk-import");

        api.MapPost("/sessions", async (
            CreateBulkImportSessionRequest request,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateSessionAsync(request, Actor(principal), cancellationToken);
                return Results.Created($"{configuredApiBasePath}/v1/bulk-import/sessions/{created.SessionId:D}", created);
            }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/sessions", async (
            int? limit,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListRecentAsync(Actor(principal), limit ?? 20, cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapGet("/sessions/{sessionId:guid}", async (
            Guid sessionId,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.GetSessionAsync(sessionId, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);

        api.MapPost("/sessions/{sessionId:guid}/items/{itemId:guid}/begin", async (
            Guid sessionId,
            Guid itemId,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.BeginItemAsync(sessionId, itemId, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPost("/sessions/{sessionId:guid}/items/{itemId:guid}/finalize", async (
            Guid sessionId,
            Guid itemId,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.FinalizeItemAsync(sessionId, itemId, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPost("/sessions/{sessionId:guid}/items/{itemId:guid}/fail", async (
            Guid sessionId,
            Guid itemId,
            BulkImportFailureRequest request,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.FailItemAsync(sessionId, itemId, request, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPost("/sessions/{sessionId:guid}/items/{itemId:guid}/retry", async (
            Guid sessionId,
            Guid itemId,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.RetryItemAsync(sessionId, itemId, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapPost("/sessions/{sessionId:guid}/cancel", async (
            Guid sessionId,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.CancelAsync(sessionId, Actor(principal), cancellationToken)); }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);

        api.MapGet("/sessions/{sessionId:guid}/report/{format}", async (
            Guid sessionId,
            string format,
            ClaimsPrincipal principal,
            IBulkImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var report = await service.GetReportAsync(sessionId, format, Actor(principal), cancellationToken);
                return Results.File(Encoding.UTF8.GetBytes(report.Content), report.ContentType, report.FileName);
            }
            catch (BulkImportRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.CatalogReadPolicy);
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";

    private static IResult Failure(BulkImportRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
}
