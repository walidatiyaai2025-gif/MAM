using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MAM.Desktop;

/// <summary>
/// Runtime localization guard for repository-controlled UI chrome that predates the centralized
/// localization pass. User-entered catalog/metadata values and technical identifiers are not
/// translated. The guard is intentionally limited to exact product strings and anchored system
/// patterns so authoritative/user data is never rewritten.
/// </summary>
public partial class MainWindow
{
    private static readonly bool LocalizationLoadedHandlerRegistered = RegisterLocalizationLoadedHandler();
    private readonly Dictionary<DependencyObject, LocalizationSnapshot> _localizationSnapshots = new(ReferenceEqualityComparer.Instance);
    private bool _localizationWired;
    private bool _localizationApplying;

    private static readonly IReadOnlyDictionary<string, string> ArabicExact = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Loading"] = "جارٍ التحميل",
        ["Empty"] = "لا توجد بيانات",
        ["API error"] = "خطأ في واجهة API",
        ["Permission denied"] = "الوصول مرفوض",
        ["Degraded"] = "حالة متدهورة",
        ["Degraded / Retry"] = "حالة متدهورة / إعادة المحاولة",
        ["Retry available"] = "إعادة المحاولة متاحة",
        ["Ready"] = "جاهز",
        ["Queued"] = "تمت الإضافة إلى قائمة الانتظار",
        ["Validation"] = "التحقق",
        ["Valid"] = "صالح",
        ["Rejected"] = "مرفوض",
        ["Saved"] = "تم الحفظ",
        ["Conflict"] = "تعارض",
        ["Invalid JSON"] = "JSON غير صالح",
        ["Exported"] = "تم التصدير",
        ["OWNER_LAST / Hardware required"] = "OWNER_LAST / أجهزة فعلية مطلوبة",
        ["Central API demo state"] = "حالة تجريبية لواجهة API المركزية",
        ["● Central API demo state"] = "● حالة تجريبية لواجهة API المركزية",
        ["NON-PRODUCTION"] = "غير إنتاجي",
        ["DIWAN AL AMIRI · DEVELOPMENT"] = "الديوان الأميري · بيئة تطوير",
        ["DEVELOPMENT DEMO · P01"] = "عرض تطويري · P01",
        ["Sign in"] = "تسجيل الدخول",
        ["User name"] = "اسم المستخدم",
        ["Password"] = "كلمة المرور",
        ["Open development workspace"] = "فتح مساحة التطوير",
        ["Switch language"] = "تبديل اللغة",
        ["Diwan Al Amiri crest"] = "شعار الديوان الأميري",
        ["Secure institutional workspace for cataloging, ingest, review, processing and governed media protection."] = "مساحة مؤسسية آمنة للفهرسة والإدخال والمراجعة والمعالجة وحماية الوسائط وفق الحوكمة.",
        ["Demo shell only. Authentication and authoritative permissions are implemented in P02."] = "واجهة عرض فقط. تتم المصادقة والصلاحيات الموثوقة عبر P02.",
        ["Demo assets"] = "أصول تجريبية",
        ["Demo protected"] = "محمي تجريبيًا",
        ["Processing"] = "قيد المعالجة",
        ["Primary + Backup verified"] = "تم التحقق من التخزين الأساسي والاحتياطي",
        ["DEMO QUEUE"] = "قائمة معالجة تجريبية",
        ["National ceremony master"] = "النسخة الرئيسية للحفل الوطني",
        ["Official reception gallery"] = "معرض الاستقبال الرسمي",
        ["Archive interview"] = "مقابلة أرشيفية",
        ["Video · 4K · 42:18"] = "فيديو · 4K · 42:18",
        ["Images · 186 files"] = "صور · 186 ملفًا",
        ["Video · HD · 18:09"] = "فيديو · HD · 18:09",
        ["Protected"] = "محمي",
        ["Backup pending"] = "النسخة الاحتياطية معلقة",
        ["Video preview shell\n00:18:42 / 00:42:18"] = "معاينة فيديو\n00:18:42 / 00:42:18",
        ["Primary verified ✓\nBackup verified ✓\nSHA-256 match ✓\nState: Protected"] = "تم التحقق من الأساسي ✓\nتم التحقق من الاحتياطي ✓\nتطابق SHA-256 ✓\nالحالة: محمي",
        ["Title · date · category · tags · preservation notes"] = "العنوان · التاريخ · التصنيف · الوسوم · ملاحظات الحفظ",
        ["Windows-only capture workspace."] = "مساحة تسجيل متاحة على Windows فقط.",
        ["Temporary local selection before central upload."] = "اختيار محلي مؤقت قبل الرفع المركزي.",
        ["P01 shell only; certified device integration arrives later."] = "واجهة P01 فقط؛ تكامل الأجهزة المعتمدة يتم في المراحل اللاحقة.",
        ["No capture device connected · Demo"] = "لا يوجد جهاز تسجيل متصل · عرض تجريبي",
        ["Temporary cache ✓ · Network: disconnected (demo) · Disk capacity ✓"] = "ذاكرة مؤقتة ✓ · الشبكة: غير متصلة (تجريبي) · سعة القرص ✓",
        ["Local cache is temporary; authoritative storage remains server-side."] = "الذاكرة المحلية مؤقتة؛ التخزين الموثوق يبقى على الخادم.",
        ["Drop files here or browse · Demo"] = "أسقط الملفات هنا أو استعرضها · عرض تجريبي",
        ["Extension · size · name · path · network readiness"] = "الامتداد · الحجم · الاسم · المسار · جاهزية الشبكة",
        ["Proxy generation"] = "إنشاء نسخة Proxy",
        ["Thumbnail generation"] = "إنشاء الصور المصغرة",
        ["Technical metadata"] = "البيانات الفنية",
        ["Backup verification"] = "التحقق من النسخة الاحتياطية",
        ["Completed"] = "مكتمل",
        ["Running 68%"] = "قيد التنفيذ 68%",
        ["Shell surface only; authoritative authorization arrives later."] = "واجهة عرض فقط؛ الصلاحيات الموثوقة تُدار عبر الخدمة المركزية.",
        ["Users"] = "المستخدمون",
        ["Roles"] = "الأدوار",
        ["Capture stations"] = "محطات التسجيل",
        ["This action requires the System Administrator role."] = "يتطلب هذا الإجراء دور مسؤول النظام.",
        ["Secrets are never displayed."] = "لا يتم عرض القيم السرية مطلقًا.",
        ["Primary: configured (demo)\nBackup: degraded (demo)\nSecrets: hidden"] = "التخزين الأساسي: مهيأ (تجريبي)\nالتخزين الاحتياطي: متدهور (تجريبي)\nالأسرار: مخفية",
        ["Loading demo catalog data…"] = "جارٍ تحميل بيانات الكتالوج التجريبية…",
        ["No assets match the current filters."] = "لا توجد أصول تطابق المرشحات الحالية.",
        ["Central API is unreachable. Retry is available."] = "تعذر الوصول إلى واجهة API المركزية. إعادة المحاولة متاحة.",
        ["You do not have permission for this action."] = "لا توجد صلاحية لتنفيذ هذا الإجراء.",
        ["Backup is unavailable; assets are not marked Protected."] = "النسخة الاحتياطية غير متاحة؛ لن تُعلّم الأصول كمحمية.",
        ["Capture device discovery failed."] = "فشل اكتشاف أجهزة التسجيل.",
        ["No capture devices are available."] = "لا توجد أجهزة تسجيل متاحة.",
        ["Recording is blocked until an approved device is available."] = "التسجيل متوقف حتى يتوفر جهاز معتمد.",
        ["Primary/Backup handoff has not started."] = "لم يبدأ التسليم إلى التخزين الأساسي/الاحتياطي.",
        ["No active capture session."] = "لا توجد جلسة تسجيل نشطة.",
        ["Select the profile, then run preflight."] = "اختر الملف التعريفي ثم نفّذ الفحص الأولي.",
        ["Central authorization policy"] = "سياسة صلاحيات مركزية",
        ["Arabic + English"] = "العربية + الإنجليزية",
        ["Explicit operational impact"] = "أثر تشغيلي صريح",
        ["Restart required"] = "إعادة تشغيل مطلوبة",
        ["Live"] = "مباشر",
        ["No policies."] = "لا توجد سياسات.",
        ["No audit events."] = "لا توجد أحداث تدقيق.",
        ["No durable queue state."] = "لا توجد حالة للقوائم الدائمة.",
        ["Filesystem roots and credentials are not exposed."] = "لا يتم كشف جذور نظام الملفات أو بيانات الاعتماد.",
        ["Primary preserved"] = "تم الحفاظ على التخزين الأساسي",
        ["Awaiting verified copy"] = "بانتظار نسخة موثقة",
        ["Never silently accepted"] = "لا يُقبل بصمت مطلقًا",
        ["Backup Pending"] = "النسخ الاحتياطي معلّق",
        ["Backup Failed"] = "فشل النسخ الاحتياطي",
        ["Mismatch"] = "عدم تطابق"
    };

    private static readonly (Regex Pattern, string Replacement)[] ArabicPatterns =
    [
        (new Regex(@"^Queued:\s*(\d+)$", RegexOptions.CultureInvariant), "تمت إضافة النسخ إلى قائمة الانتظار: $1"),
        (new Regex(@"^Integrity rechecks queued:\s*(\d+)$", RegexOptions.CultureInvariant), "تمت إضافة فحوص السلامة إلى قائمة الانتظار: $1"),
        (new Regex(@"^(\d[\d,]*) originals$", RegexOptions.CultureInvariant), "$1 نسخة أصلية"),
        (new Regex(@"^(\d[\d,]*) verified$", RegexOptions.CultureInvariant), "$1 تم التحقق منها"),
        (new Regex(@"^(\d[\d,]*) sessions$", RegexOptions.CultureInvariant), "$1 جلسة"),
        (new Regex(@"^(\d[\d,]*) enabled$", RegexOptions.CultureInvariant), "$1 مفعّلة"),
        (new Regex(@"^ReadyForUpload · (.+) bytes · SHA-256 (.+) · dropped (\d+)$", RegexOptions.CultureInvariant), "جاهز للرفع · $1 بايت · SHA-256 $2 · إطارات ساقطة $3")
    ];

    private static bool RegisterLocalizationLoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(LocalizationWindowLoaded));
        return true;
    }

    private static void LocalizationWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP12LocalizationUi();
    }

    private void WireP12LocalizationUi()
    {
        if (_localizationWired) return;
        _localizationWired = true;
        RootGrid.LayoutUpdated += (_, _) => ApplyVisibleArabicLocalization();
        LanguageButton.Click += (_, _) => Dispatcher.BeginInvoke(ApplyVisibleArabicLocalization);
        ApplyVisibleArabicLocalization();
    }

    // Kept private deliberately; rendered acceptance invokes it by reflection so the same runtime
    // translator is exercised even when the WPF tree is laid out off-screen and never Loaded.
    private void ApplyVisibleArabicLocalization()
    {
        if (_localizationApplying || RootGrid is null) return;
        _localizationApplying = true;
        try
        {
            if (!_arabic)
            {
                RestoreEnglishUi();
                return;
            }

            foreach (var node in WalkUi(RootGrid))
            {
                var snapshot = GetSnapshot(node);
                if (node is TextBlock textBlock)
                    ApplyTextTranslation(textBlock, snapshot);
                if (node is ContentControl contentControl && contentControl.Content is string content)
                    ApplyContentTranslation(contentControl, content, snapshot);
                if (node is FrameworkElement frameworkElement)
                {
                    if (frameworkElement.ToolTip is string tooltip)
                        ApplyTooltipTranslation(frameworkElement, tooltip, snapshot);
                    var automationName = AutomationProperties.GetName(frameworkElement);
                    if (!string.IsNullOrWhiteSpace(automationName))
                        ApplyAutomationTranslation(frameworkElement, automationName, snapshot);
                }
            }
        }
        finally
        {
            _localizationApplying = false;
        }
    }

    private static IEnumerable<DependencyObject> WalkUi(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            yield return current;

            foreach (var child in LogicalTreeHelper.GetChildren(current))
                if (child is DependencyObject logical) stack.Push(logical);

            try
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                    stack.Push(VisualTreeHelper.GetChild(current, i));
            }
            catch (InvalidOperationException)
            {
                // Non-visual dependency objects are already covered by the logical tree.
            }
        }
    }

    private LocalizationSnapshot GetSnapshot(DependencyObject node)
    {
        if (_localizationSnapshots.TryGetValue(node, out var snapshot)) return snapshot;
        snapshot = new LocalizationSnapshot();
        _localizationSnapshots[node] = snapshot;
        return snapshot;
    }

    private static string ToArabicProductText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (ArabicExact.TryGetValue(value, out var exact)) return exact;
        foreach (var (pattern, replacement) in ArabicPatterns)
            if (pattern.IsMatch(value)) return pattern.Replace(value, replacement);

        // These replacements are deliberately long/specific. Do not add generic English words here:
        // catalog titles, metadata, policy payloads and operator-entered values must remain untouched.
        var translated = value
            .Replace(" · attempt ", " · المحاولة ", StringComparison.Ordinal)
            .Replace(" · asset ", " · الأصل ", StringComparison.Ordinal)
            .Replace(" · pending ", " · معلّق ", StringComparison.Ordinal)
            .Replace(" · leased ", " · قيد التنفيذ ", StringComparison.Ordinal)
            .Replace(" · failed ", " · فشل ", StringComparison.Ordinal)
            .Replace(" · stale ", " · متقادم ", StringComparison.Ordinal)
            .Replace("Restart required", "إعادة تشغيل مطلوبة", StringComparison.Ordinal)
            .Replace("<not configured>", "<غير مهيأ>", StringComparison.Ordinal);
        return translated;
    }

    private void ApplyTextTranslation(TextBlock target, LocalizationSnapshot snapshot)
    {
        var current = target.Text;
        if (snapshot.TextTranslated is not null && current == snapshot.TextTranslated) return;
        var translated = ToArabicProductText(current);
        if (translated == current) return;
        snapshot.TextOriginal = current;
        snapshot.TextTranslated = translated;
        target.Text = translated;
    }

    private void ApplyContentTranslation(ContentControl target, string current, LocalizationSnapshot snapshot)
    {
        if (snapshot.ContentTranslated is not null && current == snapshot.ContentTranslated) return;
        var translated = ToArabicProductText(current);
        if (translated == current) return;
        snapshot.ContentOriginal = current;
        snapshot.ContentTranslated = translated;
        target.Content = translated;
    }

    private void ApplyTooltipTranslation(FrameworkElement target, string current, LocalizationSnapshot snapshot)
    {
        if (snapshot.TooltipTranslated is not null && current == snapshot.TooltipTranslated) return;
        var translated = ToArabicProductText(current);
        if (translated == current) return;
        snapshot.TooltipOriginal = current;
        snapshot.TooltipTranslated = translated;
        target.ToolTip = translated;
    }

    private void ApplyAutomationTranslation(FrameworkElement target, string current, LocalizationSnapshot snapshot)
    {
        if (snapshot.AutomationTranslated is not null && current == snapshot.AutomationTranslated) return;
        var translated = ToArabicProductText(current);
        if (translated == current) return;
        snapshot.AutomationOriginal = current;
        snapshot.AutomationTranslated = translated;
        AutomationProperties.SetName(target, translated);
    }

    private void RestoreEnglishUi()
    {
        foreach (var (node, snapshot) in _localizationSnapshots.ToArray())
        {
            if (node is TextBlock textBlock && snapshot.TextOriginal is not null && textBlock.Text == snapshot.TextTranslated)
                textBlock.Text = snapshot.TextOriginal;
            if (node is ContentControl contentControl && snapshot.ContentOriginal is not null && contentControl.Content as string == snapshot.ContentTranslated)
                contentControl.Content = snapshot.ContentOriginal;
            if (node is FrameworkElement frameworkElement)
            {
                if (snapshot.TooltipOriginal is not null && frameworkElement.ToolTip as string == snapshot.TooltipTranslated)
                    frameworkElement.ToolTip = snapshot.TooltipOriginal;
                if (snapshot.AutomationOriginal is not null && AutomationProperties.GetName(frameworkElement) == snapshot.AutomationTranslated)
                    AutomationProperties.SetName(frameworkElement, snapshot.AutomationOriginal);
            }
        }
        _localizationSnapshots.Clear();
    }

    private sealed class LocalizationSnapshot
    {
        public string? TextOriginal { get; set; }
        public string? TextTranslated { get; set; }
        public string? ContentOriginal { get; set; }
        public string? ContentTranslated { get; set; }
        public string? TooltipOriginal { get; set; }
        public string? TooltipTranslated { get; set; }
        public string? AutomationOriginal { get; set; }
        public string? AutomationTranslated { get; set; }
    }
}
