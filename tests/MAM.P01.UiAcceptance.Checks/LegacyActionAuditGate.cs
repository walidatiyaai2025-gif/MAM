using System.Runtime.CompilerServices;

internal static class LegacyActionAuditGate
{
    [ModuleInitializer]
    internal static void VerifyLegacyVisibleActionsAreNotSilent()
    {
        var root = FindRepositoryRoot();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var desktopGuard = Read("src/MAM.Desktop/P12LegacyButtonAuditGuard.cs");
        var webGuard = Read("src/MAM.Web/wwwroot/p12-legacy-action-guard.js");
        var index = Read("src/MAM.Web/wwwroot/index.html");

        var failures = new List<string>();
        if (!desktopGuard.Contains("Add to first collection", StringComparison.Ordinal) ||
            !desktopGuard.Contains("Visibility.Collapsed", StringComparison.Ordinal) ||
            !desktopGuard.Contains("Curation Actions", StringComparison.Ordinal))
            failures.Add("Desktop silent legacy collection shortcut must be suppressed in favor of the audited Curation Actions controls.");

        if (!webGuard.Contains("p05BindAssetActions = function", StringComparison.Ordinal) ||
            !webGuard.Contains("if (!response.ok)", StringComparison.Ordinal) ||
            !webGuard.Contains("p05InlineFailure", StringComparison.Ordinal) ||
            !webGuard.Contains("collection version", StringComparison.Ordinal))
            failures.Add("Web legacy collection shortcut must check and expose the actual Central API result.");

        var guardIndex = index.IndexOf("/p12-legacy-action-guard.js", StringComparison.Ordinal);
        var localizationIndex = index.IndexOf("/ui-localization.js", StringComparison.Ordinal);
        if (guardIndex < 0 || localizationIndex < 0 || guardIndex >= localizationIndex)
            failures.Add("Legacy action guard must load before the final localization guard.");

        if (failures.Count > 0)
            throw new InvalidOperationException("P12 legacy action audit FAILED:\n- " + string.Join("\n- ", failures));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MAM.sln"))) return current.FullName;
            current = current.Parent;
        }
        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MAM.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate MAM repository root for legacy action audit.");
    }
}
