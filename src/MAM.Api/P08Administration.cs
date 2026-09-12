using System.Security.Claims;
using MAM.Application.Administration;
using MAM.Application.Identity;
using MAM.Infrastructure.Administration;

namespace MAM.Api;

public static class P08AdministrationBootstrap
{
    public static void Add(IServiceCollection services, bool sqlConfigured)
    {
        if (sqlConfigured)
            services.AddSingleton<IAdministrationService, SqlServerAdministrationService>();
        else
            services.AddSingleton<IAdministrationService>(_ => new UnavailableAdministrationService(
                "Authoritative P08 administration requires the SQL Server policy store. Missing SQL authority is fail-closed."));

        P09OperationsBootstrap.Add(services, sqlConfigured);
    }
}

public static class P08AdministrationEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        P09OperationsBootstrap.UseCorrelation(app);
        P09OperationsEndpoints.Map(app, configuredApiBasePath);

        app.MapGet("/health/administration", async (IAdministrationService administration, CancellationToken cancellationToken) =>
        {
            var health = await administration.GetHealthAsync(cancellationToken);
            return health.IsReady
                ? Results.Ok(new { status = "Ready", administration = health })
                : Results.Json(new { status = "Degraded", administration = health }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        var admin = app.MapGroup($"{configuredApiBasePath}/v1/admin");

        admin.MapGet("/overview", async (IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.GetOverviewAsync(cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/policies", async (IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListPoliciesAsync(cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/policies/{policyKey}", async (string policyKey, IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var item = await service.GetPolicyAsync(policyKey, cancellationToken);
                return item is null ? Results.NotFound() : Results.Ok(item);
            }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapPost("/policies/{policyKey}/validate", async (string policyKey, AdminPolicyUpdateRequest request, IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ValidatePolicyAsync(policyKey, request, cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapPut("/policies/{policyKey}", async (
            string policyKey,
            AdminPolicyUpdateRequest request,
            ClaimsPrincipal principal,
            IAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.UpsertPolicyAsync(policyKey, request, Actor(principal), cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapPost("/policies/{policyKey}/test", async (
            string policyKey,
            ClaimsPrincipal principal,
            IAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.TestPolicyAsync(policyKey, Actor(principal), cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/users", async (IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListUsersAsync(cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapPut("/users/{userId:guid}", async (
            Guid userId,
            AdminUserPolicyUpdateRequest request,
            ClaimsPrincipal principal,
            IAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.UpsertUserAsync(userId, request, Actor(principal), cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/dictionaries/{dictionaryKey}", async (string dictionaryKey, IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListDictionaryAsync(dictionaryKey, cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapPut("/dictionaries/{dictionaryKey}/{entryKey}", async (
            string dictionaryKey,
            string entryKey,
            AdminDictionaryUpdateRequest request,
            ClaimsPrincipal principal,
            IAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.UpsertDictionaryEntryAsync(dictionaryKey, entryKey, request, Actor(principal), cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/audit", async (
            string? actor, string? action, string? outcome, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? limit,
            IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.QueryAuditAsync(new AdminAuditQuery(actor, action, outcome, fromUtc, toUtc, limit ?? 100), cancellationToken)); }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AuditReadPolicy);

        admin.MapGet("/audit/export", async (
            string? actor, string? action, string? outcome, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? limit,
            IAdministrationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var csv = await service.ExportAuditCsvAsync(new AdminAuditQuery(actor, action, outcome, fromUtc, toUtc, limit ?? 500), cancellationToken);
                return Results.Text(csv, "text/csv; charset=utf-8");
            }
            catch (AdministrationRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.AuditReadPolicy);
    }

    private static string Actor(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static IResult Failure(AdministrationRequestException ex) =>
        Results.Json(new { error = ex.Code, detail = ex.Message, current = ex.Current }, statusCode: ex.StatusCode);
}
