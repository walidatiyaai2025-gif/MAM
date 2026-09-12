using MAM.Application.Administration;
using MAM.Application.Clients;

namespace MAM.Web;

public static class P08AdministrationProxy
{
    public static void Map(WebApplication app, string? apiBase, string? developmentUser)
    {
        P09OperationsProxy.Map(app, apiBase, developmentUser);

        MamAdministrationApiClient? client = null;
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            var http = new HttpClient
            {
                BaseAddress = apiUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? apiUri : new Uri(apiUri.AbsoluteUri + "/"),
                Timeout = TimeSpan.FromMinutes(2)
            };
            client = new MamAdministrationApiClient(http, "WebPortal", developmentUser);
        }

        app.MapGet("/client-api/admin/health", async (CancellationToken ct) => await Execute(client, c => c.GetHealthAsync(ct)));
        app.MapGet("/client-api/admin/overview", async (CancellationToken ct) => await Execute(client, c => c.GetOverviewAsync(ct)));
        app.MapGet("/client-api/admin/policies", async (CancellationToken ct) => await Execute(client, c => c.ListPoliciesAsync(ct)));
        app.MapGet("/client-api/admin/policies/{policyKey}", async (string policyKey, CancellationToken ct) => await Execute(client, c => c.GetPolicyAsync(policyKey, ct)));
        app.MapPost("/client-api/admin/policies/{policyKey}/validate", async (string policyKey, AdminPolicyUpdateRequest request, CancellationToken ct) => await Execute(client, c => c.ValidatePolicyAsync(policyKey, request, ct)));
        app.MapPut("/client-api/admin/policies/{policyKey}", async (string policyKey, AdminPolicyUpdateRequest request, CancellationToken ct) => await Execute(client, c => c.UpdatePolicyAsync(policyKey, request, ct)));
        app.MapPost("/client-api/admin/policies/{policyKey}/test", async (string policyKey, CancellationToken ct) => await Execute(client, c => c.TestPolicyAsync(policyKey, ct)));
        app.MapGet("/client-api/admin/users", async (CancellationToken ct) => await Execute(client, c => c.ListUsersAsync(ct)));
        app.MapPut("/client-api/admin/users/{userId:guid}", async (Guid userId, AdminUserPolicyUpdateRequest request, CancellationToken ct) => await Execute(client, c => c.UpdateUserAsync(userId, request, ct)));
        app.MapGet("/client-api/admin/dictionaries/{dictionaryKey}", async (string dictionaryKey, CancellationToken ct) => await Execute(client, c => c.ListDictionaryAsync(dictionaryKey, ct)));
        app.MapPut("/client-api/admin/dictionaries/{dictionaryKey}/{entryKey}", async (string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, CancellationToken ct) => await Execute(client, c => c.UpdateDictionaryEntryAsync(dictionaryKey, entryKey, request, ct)));
        app.MapGet("/client-api/admin/audit", async (string? actor, string? action, string? outcome, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? limit, CancellationToken ct) =>
            await Execute(client, c => c.QueryAuditAsync(new AdminAuditQuery(actor, action, outcome, fromUtc, toUtc, limit ?? 100), ct)));
        app.MapGet("/client-api/admin/audit/export", async (string? actor, string? action, string? outcome, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? limit, CancellationToken ct) =>
        {
            if (client is null) return NotConfigured();
            try
            {
                var csv = await client.ExportAuditCsvAsync(new AdminAuditQuery(actor, action, outcome, fromUtc, toUtc, limit ?? 500), ct);
                return Results.Text(csv, "text/csv; charset=utf-8");
            }
            catch (MamApiException ex) { return ApiFailure(ex); }
            catch (HttpRequestException) { return Unreachable(); }
            catch (TaskCanceledException) { return Timeout(); }
        });
    }

    private static async Task<IResult> Execute<T>(MamAdministrationApiClient? client, Func<MamAdministrationApiClient, Task<T>> operation)
    {
        if (client is null) return NotConfigured();
        try { return Results.Ok(await operation(client)); }
        catch (MamApiException ex) { return ApiFailure(ex); }
        catch (HttpRequestException) { return Unreachable(); }
        catch (TaskCanceledException) { return Timeout(); }
    }

    private static IResult ApiFailure(MamApiException ex) => Results.Json(new { error = "central_api_error", status = (int)ex.StatusCode, detail = ex.Message }, statusCode: (int)ex.StatusCode);
    private static IResult NotConfigured() => Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult Unreachable() => Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult Timeout() => Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
