using System.Runtime.CompilerServices;

internal static class FunctionalButtonAuditGate
{
    [ModuleInitializer]
    internal static void VerifyUserOperableFunctionsHaveControls()
    {
        var root = FindRepositoryRoot();
        string Read(string path) => File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var failures = new List<string>();
        void Require(bool condition, string message) { if (!condition) failures.Add(message); }

        var desktopP03 = Read("src/MAM.Desktop/MainWindow.P03.cs");
        var desktopP04 = Read("src/MAM.Desktop/MainWindow.P04.cs");
        var desktopP05 = Read("src/MAM.Desktop/MainWindow.P05.cs");
        var desktopP06 = Read("src/MAM.Desktop/MainWindow.P06.cs");
        var desktopP07 = Read("src/MAM.Desktop/MainWindow.P07.cs");
        var desktopP08 = Read("src/MAM.Desktop/MainWindow.P08.cs");
        var desktopP09 = Read("src/MAM.Desktop/MainWindow.P09.cs");
        var desktopP12 = Read("src/MAM.Desktop/MainWindow.P12.FunctionActions.cs");
        var webIndex = Read("src/MAM.Web/wwwroot/index.html");
        var webP03 = Read("src/MAM.Web/wwwroot/p03-upload.js");
        var webP04 = Read("src/MAM.Web/wwwroot/p04-processing.js");
        var webP05 = Read("src/MAM.Web/wwwroot/p05-curation.js");
        var webP06 = Read("src/MAM.Web/wwwroot/p06-protection.js");
        var webP08 = Read("src/MAM.Web/wwwroot/p08-administration.js");
        var webP09 = Read("src/MAM.Web/wwwroot/p09-operations.js");
        var webP12 = Read("src/MAM.Web/wwwroot/p12-function-actions.js");
        var curationClient = Read("src/MAM.Application/Clients/MamCurationApiClient.cs");
        var adminClient = Read("src/MAM.Application/Clients/MamAdministrationApiClient.cs");
        var protectionClient = Read("src/MAM.Application/Clients/MamProtectionApiClient.cs");
        var processingContracts = Read("src/MAM.Application/Processing/ProcessingContracts.cs");

        // Upload is an explicit user action on both supported clients.
        Require(desktopP03.Contains("Choose file", StringComparison.Ordinal) && desktopP03.Contains("Start / resume", StringComparison.Ordinal),
            "Desktop resumable upload must expose Choose file and Start / resume controls.");
        Require(webP03.Contains("Start / resume", StringComparison.OrdinalIgnoreCase) || webP03.Contains("startUpload", StringComparison.OrdinalIgnoreCase),
            "Web resumable upload must expose an explicit start/resume control.");

        // Processing profiles that are user-operable must be reachable through explicit buttons.
        foreach (var token in new[] { "BuiltInProcessingProfiles.Inspect", "BuiltInProcessingProfiles.VideoProxy", "BuiltInProcessingProfiles.ImagePreview", "BuiltInProcessingProfiles.AudioPreview", "BuiltInProcessingProfiles.PdfInline" })
            Require(desktopP04.Contains(token, StringComparison.Ordinal), $"Desktop processing control is missing: {token}");
        foreach (var profile in new[] { "inspect-v1", "video-proxy-v1", "image-preview-v1", "audio-preview-v1", "pdf-inline-v1" })
            Require(webP04.Contains(profile, StringComparison.Ordinal), $"Web processing control is missing: {profile}");
        Require(processingContracts.Contains("PdfInline = \"pdf-inline-v1\"", StringComparison.Ordinal), "PDF processing profile contract changed unexpectedly.");
        Require(desktopP04.Contains("retryState.Text", StringComparison.Ordinal) && !desktopP04.Contains("try { await _p04ProcessingClient.RetryAsync(job.JobId); } catch { }", StringComparison.Ordinal),
            "Desktop processing Retry must expose its success/failure instead of swallowing errors.");
        Require(webP04.Contains("if(!retry.ok)", StringComparison.Ordinal), "Web processing Retry must check the actual API result.");

        // Existing curation controls plus the audit-added missing capabilities.
        foreach (var token in new[] { "UpdateMetadataAsync", "CreateCollectionAsync", "AddToCollectionAsync", "ArchiveAsync", "RestoreAsync" })
            Require(desktopP05.Contains(token, StringComparison.Ordinal) || webP05.Contains(token.Replace("Async", string.Empty), StringComparison.OrdinalIgnoreCase) || webP05.Contains(token, StringComparison.Ordinal),
                $"Existing curation function lost its UI wiring: {token}");
        Require(curationClient.Contains("BulkUpdateMetadataAsync", StringComparison.Ordinal) && desktopP12.Contains("BulkUpdateMetadataAsync", StringComparison.Ordinal) && webP12.Contains("/client-api/curation/assets/bulk-metadata", StringComparison.Ordinal),
            "Bulk metadata capability must have explicit Desktop and Web controls wired to the Central API.");
        Require(curationClient.Contains("RemoveFromCollectionAsync", StringComparison.Ordinal) && desktopP12.Contains("RemoveFromCollectionAsync", StringComparison.Ordinal) && webP12.Contains("p12CollectionRemove", StringComparison.Ordinal),
            "Remove-from-collection capability must have explicit Desktop and Web controls.");
        Require(desktopP12.Contains("Add to collection", StringComparison.Ordinal) && webP12.Contains("Add to collection", StringComparison.Ordinal),
            "Collection add must remain an explicit action on both clients.");

        // Protection must not share the Administration or Asset route; it gets a first-class entry point.
        Require(desktopP06.Contains("Tag = \"protection\"", StringComparison.Ordinal) && desktopP06.Contains("_currentRoute != \"protection\"", StringComparison.Ordinal),
            "Desktop Backup Protection must use its own navigation route.");
        Require(!desktopP06.Contains("route is not (\"admin\" or \"asset\")", StringComparison.Ordinal),
            "Desktop Backup Protection must not compete with Administration/Asset handlers.");
        Require(webP06.Contains("pages.protection", StringComparison.Ordinal) && webP06.Contains("route === 'protection'", StringComparison.Ordinal),
            "Web Backup Protection must use its own navigation route.");
        Require(!webP06.Contains("if (route === 'admin') void loadProtectionAdmin()", StringComparison.Ordinal) && !webP06.Contains("if (route === 'asset') void loadProtectionAsset()", StringComparison.Ordinal),
            "Web Backup Protection must not overwrite Administration or Asset Details.");
        foreach (var token in new[] { "QueueAsync", "QueueIntegrityRecheckAsync", "GetAssetAsync" })
            Require(protectionClient.Contains(token, StringComparison.Ordinal) && desktopP06.Contains(token, StringComparison.Ordinal), $"Desktop protection control is missing Central API action: {token}");
        foreach (var token in new[] { "p06Queue", "p06Recheck", "p06Lookup" })
            Require(webP06.Contains(token, StringComparison.Ordinal), $"Web protection control is missing: {token}");

        // Tape capture is Windows-only, and every user-operable lifecycle operation must remain visible.
        foreach (var label in new[] { "Run preflight", "Start recording", "Refresh status", "Stop, finalize & upload" })
            Require(desktopP07.Contains(label, StringComparison.Ordinal), $"Windows Tape Capture control is missing: {label}");
        Require(!webIndex.Contains("data-route=\"capture\"", StringComparison.OrdinalIgnoreCase), "Web must not expose the Windows-only Tape Capture action.");

        // Administration: policy actions already existed; audit adds explicit user/role and dictionary actions to both clients.
        foreach (var label in new[] { "Validate", "Test reference", "Save" })
            Require(webP08.Contains(label, StringComparison.OrdinalIgnoreCase), $"Web Administration policy control is missing: {label}");
        Require(webP08.Contains("Export CSV", StringComparison.Ordinal), "Web Administration audit export must remain explicit.");
        foreach (var method in new[] { "UpdateUserAsync", "UpdateDictionaryEntryAsync" })
            Require(adminClient.Contains(method, StringComparison.Ordinal) && desktopP12.Contains(method, StringComparison.Ordinal), $"Desktop Management Actions are missing: {method}");
        Require(desktopP12.Contains("ExportAuditCsvAsync", StringComparison.Ordinal), "Desktop must expose an Audit CSV export action.");
        foreach (var endpoint in new[] { "/client-api/admin/users/", "/client-api/admin/dictionaries/" })
            Require(webP12.Contains(endpoint, StringComparison.Ordinal), $"Web Management Actions are missing endpoint wiring: {endpoint}");
        Require(webP12.Contains("Save user & roles", StringComparison.Ordinal) && webP12.Contains("Save dictionary entry", StringComparison.Ordinal),
            "Web Management Actions must expose clear save buttons for users/roles and dictionaries.");

        // Operational diagnostics are a user-triggered read action; health/list views load from navigation by design.
        Require(desktopP09.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) && webP09.Contains("p09Diagnostics", StringComparison.Ordinal),
            "Operations diagnostics must have an explicit user control on Desktop and Web.");

        // The action-center script must load before the final localization guard so added controls stay bilingual.
        Require(webIndex.Contains("/p12-function-actions.js", StringComparison.Ordinal), "Web action-center script is not loaded.");
        Require(webIndex.IndexOf("/p12-function-actions.js", StringComparison.Ordinal) < webIndex.IndexOf("/ui-localization.js", StringComparison.Ordinal),
            "Web action-center must load before the final localization guard.");

        // Important boundary: background worker internals are intentionally NOT buttons. The UI audit covers
        // user-operable capabilities only. LeaseNext/Heartbeat/Process are service/worker responsibilities.
        Require(processingContracts.Contains("LeaseNextAsync", StringComparison.Ordinal) && processingContracts.Contains("HeartbeatAsync", StringComparison.Ordinal) && processingContracts.Contains("ProcessAsync", StringComparison.Ordinal),
            "Processing worker-internal boundary changed; review the function-button audit classification.");

        if (failures.Count == 0) return;
        throw new InvalidOperationException("P12 functional button audit FAILED:\n- " + string.Join("\n- ", failures));
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
        throw new InvalidOperationException("Could not locate MAM repository root for functional button audit.");
    }
}
