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
var desktopLocalization = Read("src/MAM.Desktop/MainWindow.Localization.cs");
var webIndex = Read("src/MAM.Web/wwwroot/index.html");
var webCss = Read("src/MAM.Web/wwwroot/styles.css");
var webJs = Read("src/MAM.Web/wwwroot/app.js");
var webLocalization = Read("src/MAM.Web/wwwroot/ui-localization.js");
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

var webHasRtl = webJs.Contains("\"rtl\"", StringComparison.Ordinal) || webJs.Contains("'rtl'", StringComparison.Ordinal);
var webHasLtr = webJs.Contains("\"ltr\"", StringComparison.Ordinal) || webJs.Contains("'ltr'", StringComparison.Ordinal);
Require(webJs.Contains("document.documentElement.dir", StringComparison.Ordinal) && webHasRtl && webHasLtr,
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

// P12 bilingual-completeness audit. Earlier acceptance proved RTL/LTR direction, but it did not
// prove that legacy phase modules stopped showing English UI chrome after the Arabic switch.
Require(desktopLocalization.Contains("ApplyVisibleArabicLocalization", StringComparison.Ordinal),
    "Desktop localization completeness guard is missing.");
Require(desktopLocalization.Contains("User-entered catalog/metadata values", StringComparison.Ordinal),
    "Desktop localization guard must explicitly preserve authoritative/user data.");
Require(desktopLocalization.Contains("AutomationProperties", StringComparison.Ordinal),
    "Desktop localization guard must cover accessibility names as well as visible text.");
Require(webIndex.Contains("<script src=\"/ui-localization.js\"></script>", StringComparison.Ordinal),
    "Web localization guard must be loaded by the product shell.");
Require(webIndex.IndexOf("/ui-localization.js", StringComparison.Ordinal) > webIndex.IndexOf("/p09-operations.js", StringComparison.Ordinal),
    "Web localization guard must load after every phase UI module.");
Require(webLocalization.Contains("MutationObserver", StringComparison.Ordinal),
    "Web localization guard must cover asynchronous UI updates.");
Require(webLocalization.Contains("placeholder", StringComparison.Ordinal) &&
        webLocalization.Contains("aria-label", StringComparison.Ordinal) &&
        webLocalization.Contains("title", StringComparison.Ordinal) &&
        webLocalization.Contains("alt", StringComparison.Ordinal),
    "Web localization guard must cover accessibility and input chrome, not visible text only.");
Require(webLocalization.Contains("user/catalog metadata", StringComparison.OrdinalIgnoreCase),
    "Web localization guard must explicitly preserve user/catalog metadata.");

var requiredArabicTranslations = new[]
{
    "جارٍ التحميل", "لا توجد بيانات", "خطأ في واجهة API", "الوصول مرفوض", "حالة متدهورة",
    "إعادة المحاولة متاحة", "المستخدمون", "الأدوار", "البيانات الوصفية", "التخزين الأساسي",
    "التخزين الاحتياطي", "البحث والمرشحات", "المجموعات والسياسة", "حماية النسخة الاحتياطية",
    "إدارة المؤسسة والسياسات", "التقارير والمراقبة والتعافي", "صحة الاعتمادات", "حزمة التشخيص"
};
foreach (var translation in requiredArabicTranslations)
{
    Require(desktopLocalization.Contains(translation, StringComparison.Ordinal) ||
            webLocalization.Contains(translation, StringComparison.Ordinal),
        $"Bilingual completeness catalog is missing required Arabic product text: {translation}");
}

foreach (var module in new[]
{
    "src/MAM.Desktop/MainWindow.P02.cs", "src/MAM.Desktop/MainWindow.P03.cs", "src/MAM.Desktop/MainWindow.P04.cs",
    "src/MAM.Desktop/MainWindow.P05.cs", "src/MAM.Desktop/MainWindow.P06.cs", "src/MAM.Desktop/MainWindow.P07.cs",
    "src/MAM.Desktop/MainWindow.P08.cs", "src/MAM.Desktop/MainWindow.P09.cs",
    "src/MAM.Web/wwwroot/p03-upload.js", "src/MAM.Web/wwwroot/p04-processing.js", "src/MAM.Web/wwwroot/p05-curation.js",
    "src/MAM.Web/wwwroot/p06-protection.js", "src/MAM.Web/wwwroot/p08-administration.js", "src/MAM.Web/wwwroot/p09-operations.js"
})
{
    var source = Read(module);
    Require(source.Any(ch => ch is >= '\u0600' and <= '\u06FF'), $"Product UI module contains no Arabic localization path: {module}");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("P01 UI acceptance FAILED:");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("P01 UI contract acceptance passed.");
Console.WriteLine($"Crest SHA-256: {BrandTokens.CrestSha256}");
Console.WriteLine("Desktop: premium shell + Windows-only Tape Capture + RTL/LTR + state/accessibility baseline + centralized Arabic completeness guard.");
Console.WriteLine("Web: responsive 360/tablet/1440-safe shell + RTL/LTR + asynchronous Arabic completeness guard; Tape Capture excluded.");
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
