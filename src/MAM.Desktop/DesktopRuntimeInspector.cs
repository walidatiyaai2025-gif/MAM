using System.Net.Http.Json;
using System.Text.Json;
using MAM.Application.Diagnostics;

namespace MAM.Desktop;

internal static class DesktopRuntimeInspector
{
    private static readonly RuntimeInspectorLog Local = new("MAM.Desktop", BuildInfo.Current);

    public static void Install(System.Windows.Application app)
    {
        Local.Write(new RuntimeDiagnosticEvent(
            "Information", "process-start",
            $"MAM.Desktop started. Local diagnostic fallback: {Local.RootPath}"));

        app.DispatcherUnhandledException += (_, e) =>
            Capture(e.Exception, "dispatcher-unhandled-exception");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Capture(e.ExceptionObject as Exception ?? new Exception("Non-Exception fatal error."), "appdomain-unhandled-exception");

        TaskScheduler.UnobservedTaskException += (_, e) =>
            Capture(e.Exception, "unobserved-task-exception");
    }

    public static void Capture(Exception ex, string kind, string? route = null, string? correlationId = null)
    {
        var entry = new RuntimeDiagnosticEvent(
            "Error",
            kind,
            ex.Message,
            ex.GetType().FullName,
            ex.ToString(),
            correlationId,
            route,
            User: DesktopProductionTransport.AuthenticatedUser,
            Metadata: new Dictionary<string, string?>
            {
                ["environment"] = DesktopProductionTransport.EnvironmentLabel
            });

        Local.Write(entry);
        _ = SendToServerAsync(entry);
    }

    public static void CaptureMessage(string level, string kind, string message, string? route = null, string? correlationId = null)
    {
        var entry = new RuntimeDiagnosticEvent(
            level, kind, message,
            CorrelationId: correlationId,
            Route: route,
            User: DesktopProductionTransport.AuthenticatedUser);
        Local.Write(entry);
        _ = SendToServerAsync(entry);
    }

    private static async Task SendToServerAsync(RuntimeDiagnosticEvent entry)
    {
        if (!DesktopProductionTransport.IsProduction ||
            string.IsNullOrWhiteSpace(DesktopProductionTransport.AuthenticatedUser))
            return;

        try
        {
            using var client = DesktopProductionTransport.CreateApiClient(TimeSpan.FromSeconds(8));
            if (client is null) return;

            using var response = await client.PostAsJsonAsync(
                "api/v1/runtime-inspector/client-event",
                new
                {
                    level = entry.Level,
                    kind = entry.Kind,
                    message = entry.Message,
                    exceptionType = entry.ExceptionType,
                    stack = entry.Stack,
                    correlationId = entry.CorrelationId,
                    route = entry.Route,
                    method = entry.Method,
                    status = entry.Status,
                    metadata = entry.Metadata
                });
        }
        catch
        {
            // The local JSONL record is the durable fallback when the server/session is unavailable.
        }
    }
}
