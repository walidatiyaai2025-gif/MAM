using MAM.Application.Diagnostics;

namespace MAM.MacUploader;

internal static class MacRuntimeInspector
{
    private static readonly RuntimeInspectorLog Local = new("MAM.MacUploader", BuildInfo.Current);

    public static void Install()
    {
        Local.Write(new RuntimeDiagnosticEvent(
            "Information", "process-start",
            $"MAM.MacUploader started. Local diagnostic fallback: {Local.RootPath}"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Capture(e.ExceptionObject as Exception ?? new Exception("Non-Exception fatal error."), "appdomain-unhandled-exception");

        TaskScheduler.UnobservedTaskException += (_, e) =>
            Capture(e.Exception, "unobserved-task-exception");
    }

    public static void Capture(Exception ex, string kind, string? user = null)
    {
        Local.Write(new RuntimeDiagnosticEvent(
            "Error",
            kind,
            ex.Message,
            ex.GetType().FullName,
            ex.ToString(),
            User: user,
            Metadata: new Dictionary<string, string?> { ["platform"] = "macOS" }));
    }
}
