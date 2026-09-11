using MAM.Application.Branding;

var failures = new List<string>();

void Require(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

Require(DiwanCrestData.Bytes.Length == 69136, "Approved crest byte length must remain 69136.");
Require(DiwanCrestData.HasApprovedFingerprint(), "Approved crest SHA-256 fingerprint must match the owner-supplied source.");
Require(BrandTokens.CrestSha256 == "bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb", "Approved crest fingerprint constant changed.");
Require(BrandTokens.Navy900 == "#0A2342" && BrandTokens.Gold600 == "#B58A2A", "Locked Navy/Gold tokens changed.");

var root = FindRepositoryRoot();
var desktopXaml = Read("src/MAM.Desktop/MainWindow.xaml");
var desktopCode = Read("src/MAM.Desktop/MainWindow.xaml.cs");
var webIndex = Read("src/MAM.Web/wwwroot/index.html");
var webCss = Read("src/MAM.Web/wwwroot/styles.css");
var webJs = Read("src/MAM.Web/wwwroot/app.js");
var webProgram = Read("src/MAM.Web/Program.cs");

foreach (var required in new[]
{
    "Dashboard", "Media Library", "Asset Details", "New Ingest", "Tape Capture",
    "Upload", "Processing Queue", "Administration", "Settings", "DEVELOPMENT DEMO"
})
{
    Require(desktopXaml.Contains(required, StringComparison.OrdinalIgnoreCase) ||
            desktopCode.Contains(required, StringComparison.OrdinalIgnoreCase),
        $"Desktop shell is missing required surface/state text: {required}");
}

foreach (var required in new[]
{
    "Dashboard", "Media Library", "Asset Details", "New Ingest", "Upload",
    "Processing Queue", "Administration", "Settings", "DEVELOPMENT DEMO"
})
{
    Require(webIndex.Contains(required, StringComparison.OrdinalIgnoreCase) ||
            webJs.Contains(required, StringComparison.OrdinalIgnoreCase),
        $"Web shell is missing required surface/state text: {required}");
}

Require(!webIndex.Contains("data-route=\"capture\"", StringComparison.OrdinalIgnoreCase),
    "Web navigation must not expose Windows-only Tape Capture.");
Require(webJs.Contains("Windows-only capability", StringComparison.OrdinalIgnoreCase),
    "Web ingest must explain that Tape Capture is Windows-only.");

foreach (var state in new[] { "Loading", "Empty", "API error", "Permission denied", "Degraded" })
{
    Require(desktopCode.Contains(state, StringComparison.OrdinalIgnoreCase), $"Desktop missing {state} treatment.");
    Require(webJs.Contains(state, StringComparison.OrdinalIgnoreCase) || webIndex.Contains(state, StringComparison.OrdinalIgnoreCase),
        $"Web missing {state} treatment.");
}

Require(desktopCode.Contains("FlowDirection.RightToLeft", StringComparison.Ordinal) &&
        desktopCode.Contains("FlowDirection.LeftToRight", StringComparison.Ordinal),
    "Desktop must support first-class RTL and LTR direction switching.");
Require(webJs.Contains("document.documentElement.dir", StringComparison.Ordinal) &&
        webJs.Contains("\"rtl\"", StringComparison.Ordinal) &&
        webJs.Contains("\"ltr\"", StringComparison.Ordinal),
    "Web must support first-class RTL and LTR direction switching.");

foreach (var breakpoint in new[] { "1180px", "820px", "520px", "360px" })
{
    Require(webCss.Contains(breakpoint, StringComparison.Ordinal), $"Web responsive breakpoint missing: {breakpoint}");
}
Require(webCss.Contains(":focus-visible", StringComparison.Ordinal), "Web keyboard focus treatment is missing.");
Require(webCss.Contains("prefers-reduced-motion", StringComparison.Ordinal), "Web reduced-motion accessibility treatment is missing.");
Require(desktopXaml.Contains("IsKeyboardFocused", StringComparison.Ordinal), "Desktop keyboard focus treatment is missing.");
Require(desktopXaml.Contains("UseLayoutRounding=\"True\"", StringComparison.Ordinal), "Desktop high-DPI layout rounding baseline is missing.");

Require(webProgram.Contains("HasApprovedFingerprint", StringComparison.Ordinal) &&
        webProgram.Contains("Results.File", StringComparison.Ordinal),
    "Web crest endpoint must fail closed on fingerprint mismatch.");

if (failures.Count > 0)
{
    Console.Error.WriteLine("P01 UI acceptance FAILED:");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("P01 UI contract acceptance passed.");
Console.WriteLine($"Crest SHA-256: {BrandTokens.CrestSha256}");
Console.WriteLine("Desktop: premium shell + Windows-only Tape Capture + RTL/LTR + state/accessibility baseline.");
Console.WriteLine("Web: responsive 360/tablet/1440-safe shell + RTL/LTR + state/accessibility baseline; Tape Capture excluded.");
return 0;

string Read(string relativePath)
{
    var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(full))
    {
        failures.Add($"Missing required file: {relativePath}");
        return string.Empty;
    }
    return File.ReadAllText(full);
}

string FindRepositoryRoot()
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

    throw new InvalidOperationException("Could not locate MAM repository root.");
}
