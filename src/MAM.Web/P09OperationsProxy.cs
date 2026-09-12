using MAM.Application.Clients;

namespace MAM.Web;

public static class P09OperationsProxy
{
    public static void Map(WebApplication app, string? apiBase, string? developmentUser)
    {
        MamOperationsApiClient? client = null;
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            var http = new HttpClient
            {
                BaseAddress = apiUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? apiUri : new Uri(apiUri.AbsoluteUri + "/"),
                Timeout = TimeSpan.FromMinutes(2)
            };
            client = new MamOperationsApiClient(http, "WebPortal", developmentUser);
        }

        app.MapGet("/client-api/operations/health", async (CancellationToken ct) => await Execute(client, c => c.GetHealthAsync(ct)));
        app.MapGet("/client-api/operations/summary", async (CancellationToken ct) => await Execute(client, c => c.GetSummaryAsync(ct)));
        app.MapGet("/client-api/operations/throughput", async (int? windowHours, CancellationToken ct) => await Execute(client, c => c.GetThroughputAsync(windowHours ?? 24, ct)));
        app.MapGet("/client-api/operations/queues", async (CancellationToken ct) => await Execute(client, c => c.GetQueuesAsync(ct)));
        app.MapGet("/client-api/operations/integrity", async (CancellationToken ct) => await Execute(client, c => c.GetIntegrityAsync(ct)));
        app.MapGet("/client-api/operations/storage", async (CancellationToken ct) => await Execute(client, c => c.GetStorageAsync(ct)));
        app.MapGet("/client-api/operations/dependencies", async (CancellationToken ct) => await Execute(client, c => c.GetDependenciesAsync(ct)));
        app.MapGet("/client-api/operations/diagnostics", async (CancellationToken ct) => await Execute(client, c => c.GetDiagnosticsAsync(ct)));
    }

    private static async Task<IResult> Execute<T>(MamOperationsApiClient? client, Func<MamOperationsApiClient, Task<T>> operation)
    {
        if (client is null) return Results.Json(new { error = "central_api_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        try { return Results.Ok(await operation(client)); }
        catch (MamApiException ex) { return Results.Json(new { error = "central_api_error", status = (int)ex.StatusCode }, statusCode: (int)ex.StatusCode); }
        catch (HttpRequestException) { return Results.Json(new { error = "central_api_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (TaskCanceledException) { return Results.Json(new { error = "central_api_timeout" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }
}
