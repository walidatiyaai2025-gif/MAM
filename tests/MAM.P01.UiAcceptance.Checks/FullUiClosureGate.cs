using System.Runtime.CompilerServices;

internal static class FullUiClosureGate
{
    [ModuleInitializer]
    internal static void VerifyFullUiClosureContract()
    {
        var root = FindRepositoryRoot();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var index = Read("src/MAM.Web/wwwroot/index.html");
        var runtime = Read("src/MAM.Web/wwwroot/p131-experience-closure.js");
        var styles = Read("src/MAM.Web/wwwroot/p131-experience-closure.css");
        var failures = new List<string>();
        void Require(bool value, string message) { if (!value) failures.Add(message); }

        Require(index.Contains("/p131-experience-closure.css", StringComparison.Ordinal) &&
                index.Contains("/p131-experience-closure.js", StringComparison.Ordinal),
            "The full UI/UX closure layer must be loaded by the application shell.");
        Require(index.IndexOf("/p131-experience-closure.js", StringComparison.Ordinal) > index.IndexOf("/p130-menu-fix.js", StringComparison.Ordinal),
            "The closure runtime must load after all historical UI layers.");

        foreach (var route in new[]
        {
            "dashboard", "library", "asset", "ingest", "upload", "queue", "reports", "protection",
            "admin", "settings", "categories", "references", "mediaPermissions", "admin-actions", "search", "myPermissions"
        })
            Require(runtime.Contains($"'{route}'", StringComparison.Ordinal), $"Full UI route inventory is missing '{route}'.");

        foreach (var stateKey in new[] { "asset", "q", "kind", "lifecycle", "category", "collection", "tag", "page", "view", "sort", "tab", "searched" })
            Require(runtime.Contains($"'{stateKey}'", StringComparison.Ordinal), $"Language-switch route state does not preserve '{stateKey}'.");

        Require(runtime.Contains("window.addEventListener('hashchange'", StringComparison.Ordinal),
            "Deep-link navigation must restore the requested logical route.");
        Require(runtime.Contains("url.searchParams.set('lang'", StringComparison.Ordinal) && runtime.Contains("history.replaceState", StringComparison.Ordinal),
            "Language switching must update locale without replacing the logical route.");
        Require(runtime.Contains("aria-current", StringComparison.Ordinal) && runtime.Contains("aria-expanded", StringComparison.Ordinal),
            "Navigation selection and disclosure state must remain accessible.");
        Require(styles.Contains("@media(max-width:800px)", StringComparison.Ordinal) && styles.Contains("@media(max-width:420px)", StringComparison.Ordinal),
            "The closure design must include tablet/mobile responsive rules.");
        Require(styles.Contains("inset-inline", StringComparison.Ordinal) && styles.Contains("padding-inline", StringComparison.Ordinal),
            "The closure design must use logical properties for RTL/LTR equivalence.");
        Require(styles.Contains(":focus", StringComparison.Ordinal) && styles.Contains("prefers-reduced-motion", StringComparison.Ordinal),
            "The closure design must preserve focus and reduced-motion accessibility.");

        if (failures.Count > 0)
            throw new InvalidOperationException("P12.11 full UI closure contract FAILED:\n- " + string.Join("\n- ", failures));
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
        throw new InvalidOperationException("Could not locate MAM repository root for full UI closure audit.");
    }
}
