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
        var bootstrap = Read("src/MAM.Web/wwwroot/p131-route-bootstrap.js");
        var standalone = Read("src/MAM.Web/wwwroot/p131-standalone-localization.js");
        var styles = Read("src/MAM.Web/wwwroot/p131-experience-closure.css");
        var referenceGapStyles = Read("src/MAM.Web/wwwroot/p132-reference-gap.css");
        var upload = Read("src/MAM.Web/wwwroot/p03-upload.js");
        var reports = Read("src/MAM.Web/wwwroot/p09-operations.js");
        var landing = Read("src/MAM.Web/wwwroot/landing.html");
        var login = Read("src/MAM.Web/wwwroot/login.html");
        var failures = new List<string>();
        void Require(bool value, string message) { if (!value) failures.Add(message); }

        Require(index.Contains("/p131-experience-closure.css", StringComparison.Ordinal) &&
                index.Contains("/p131-experience-closure.js", StringComparison.Ordinal),
            "The full UI/UX closure layer must be loaded by the application shell.");
        Require(index.Contains("/p132-reference-gap.css", StringComparison.Ordinal) &&
                index.IndexOf("/p132-reference-gap.css", StringComparison.Ordinal) > index.IndexOf("/p131-experience-closure.css", StringComparison.Ordinal),
            "The targeted P13.2 reference-gap styles must load after the prior approved shell without replacing it.");
        Require(index.IndexOf("/p131-experience-closure.js", StringComparison.Ordinal) > index.IndexOf("/p130-menu-fix.js", StringComparison.Ordinal),
            "The closure runtime must load after all historical UI layers.");
        Require(index.Contains("/p131-route-bootstrap.js", StringComparison.Ordinal) &&
                bootstrap.Contains("mamP131InitialHash", StringComparison.Ordinal) && runtime.Contains("mamP131InitialHash", StringComparison.Ordinal),
            "Deep-link state must be captured before legacy route initialization can normalize the hash.");
        Require(landing.Contains("/p131-standalone-localization.js", StringComparison.Ordinal) &&
                login.Contains("/p131-standalone-localization.js", StringComparison.Ordinal),
            "Landing and login must load the shared bilingual standalone-page closure.");
        Require(standalone.Contains("url.searchParams.set('lang'", StringComparison.Ordinal) &&
                standalone.Contains("returnUrl", StringComparison.Ordinal) && standalone.Contains("loginErrors", StringComparison.Ordinal),
            "Standalone pages must preserve language in the URL, login return path, and error states.");

        foreach (var route in new[]
        {
            "dashboard", "library", "asset", "curation-actions", "ingest", "upload", "queue", "reports", "protection",
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

        Require(upload.Contains("p132-upload-layout", StringComparison.Ordinal) &&
                upload.Contains("p132Dropzone", StringComparison.Ordinal) &&
                upload.Contains("p132DetectedKind", StringComparison.Ordinal) &&
                upload.Contains("p03ApplyOptionalMetadata", StringComparison.Ordinal) &&
                upload.Contains("p03QueueAutomaticProcessing", StringComparison.Ordinal),
            "Add Media must use the approved compact upload composition while retaining real upload, metadata and processing behavior.");
        Require(!upload.Contains("Durable Primary Upload", StringComparison.Ordinal),
            "The legacy upload-page presentation must not remain reachable.");
        Require(reports.Contains("p132-report-kpis", StringComparison.Ordinal) &&
                reports.Contains("data-report-panel=\"overview\"", StringComparison.Ordinal) &&
                reports.Contains("data-report-panel=\"protection\"", StringComparison.Ordinal) &&
                reports.Contains("p09Diagnostics", StringComparison.Ordinal),
            "Reports must use the approved compact KPI/tab composition while retaining authoritative operational diagnostics.");
        Require(referenceGapStyles.Contains("@media(max-width:1024px)", StringComparison.Ordinal) &&
                referenceGapStyles.Contains("@media(max-width:430px)", StringComparison.Ordinal) &&
                referenceGapStyles.Contains("[dir=\"rtl\"] .p132-upload-layout", StringComparison.Ordinal),
            "P13.2 reference surfaces must explicitly cover tablet/mobile and RTL mirroring.");

        if (failures.Count > 0)
            throw new InvalidOperationException("P13.2 full UI gap-closure contract FAILED:\n- " + string.Join("\n- ", failures));
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
