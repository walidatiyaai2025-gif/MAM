using System.Security.Claims;
using MAM.Api.Security;
using MAM.Application.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace MAM.Api;

internal static class RuntimeInspectorEndpoints
{
    public static void Map(RouteGroupBuilder api, RuntimeInspectorLog inspector)
    {
        api.MapPost("/runtime-inspector/client-event", (
            RuntimeClientEvent request,
            HttpContext context) =>
        {
            var correlationId = HeaderOr(request.CorrelationId, context.Request.Headers["X-Correlation-ID"].FirstOrDefault())
                                ?? Guid.NewGuid().ToString("D");

            inspector.Write(new RuntimeDiagnosticEvent(
                Level: string.IsNullOrWhiteSpace(request.Level) ? "Error" : request.Level!,
                Kind: string.IsNullOrWhiteSpace(request.Kind) ? "client-runtime" : request.Kind!,
                Message: request.Message,
                ExceptionType: request.ExceptionType,
                Stack: request.Stack,
                CorrelationId: correlationId,
                Route: request.Route,
                Method: request.Method,
                Status: request.Status,
                User: context.User.Identity?.Name,
                Metadata: request.Metadata));

            return Results.Accepted(value: new { accepted = true, correlationId });
        }).RequireAuthorization();

        api.MapGet("/runtime-inspector/latest", (HttpContext context) =>
        {
            var path = inspector.LatestFile;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Results.NotFound(new { error = "runtime_log_not_found", logRoot = inspector.RootPath });

            return Results.File(
                path,
                contentType: "text/plain; charset=utf-8",
                fileDownloadName: Path.GetFileName(path),
                enableRangeProcessing: true);
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        api.MapGet("/runtime-inspector/status", () => Results.Ok(new
        {
            enabled = true,
            format = "JSONL text",
            logRoot = inspector.RootPath,
            latestFile = inspector.LatestFile is null ? null : Path.GetFileName(inspector.LatestFile),
            maxSegmentMb = 20
        })).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private static string? HeaderOr(string? primary, string? secondary) =>
        !string.IsNullOrWhiteSpace(primary) ? primary.Trim() :
        !string.IsNullOrWhiteSpace(secondary) ? secondary.Trim() : null;
}

internal sealed record RuntimeClientEvent(
    string? Level,
    string? Kind,
    string? Message,
    string? ExceptionType,
    string? Stack,
    string? CorrelationId,
    string? Route,
    string? Method,
    int? Status,
    IReadOnlyDictionary<string, string?>? Metadata);
