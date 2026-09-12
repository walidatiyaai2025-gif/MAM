using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MAM.Desktop;

/// <summary>
/// Arabic presentation guard for dynamic Windows Tape Capture diagnostics. Capture provider
/// identifiers, device/profile values, SHA-256 values and operator-entered Tape IDs remain exact;
/// repository-controlled severity, preflight, telemetry and failure prose is localized.
/// </summary>
public partial class MainWindow
{
    private static readonly bool P07LocalizationLoadedHandlerRegistered = RegisterP07LocalizationLoadedHandler();
    private bool _p07LocalizationWired;
    private bool _p07LocalizationApplying;

    private static readonly IReadOnlyDictionary<string, string> P07RuntimeArabic = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Capture device is unavailable."] = "جهاز التسجيل غير متاح.",
        ["Selected capture profile is unsupported by the device."] = "ملف التسجيل المحدد غير مدعوم بواسطة الجهاز.",
        ["Temporary ingest cache is unavailable."] = "ذاكرة الإدخال المؤقتة غير متاحة.",
        ["Required cache capacity must be positive."] = "يجب أن تكون سعة الذاكرة المؤقتة المطلوبة أكبر من صفر.",
        ["Temporary ingest cache has insufficient free capacity."] = "المساحة الحرة في ذاكرة الإدخال المؤقتة غير كافية.",
        ["Central API is currently unreachable. Recording may continue only under an approved recovery policy; automatic authoritative handoff is unavailable."] = "واجهة API المركزية غير متاحة حاليًا. لا يستمر التسجيل إلا وفق سياسة استرداد معتمدة، والتسليم الموثوق التلقائي غير متاح.",
        ["MAM_CAPTURE_REQUIRED_CACHE_BYTES must be explicitly configured."] = "يجب تهيئة MAM_CAPTURE_REQUIRED_CACHE_BYTES صراحةً.",
        ["Central API is unavailable and offline recovery policy is not explicitly approved."] = "واجهة API المركزية غير متاحة وسياسة الاسترداد دون اتصال غير معتمدة صراحةً.",
        ["Tape ID, device profile, container and codec must be explicitly configured."] = "يجب تحديد معرف الشريط وملف الجهاز والحاوية والترميز صراحةً.",
        ["Capture did not finalize into a recoverable artifact."] = "لم يكتمل التسجيل إلى ملف قابل للاسترداد.",
        ["Local finalized capture does not match provider length/SHA-256 evidence."] = "ملف التسجيل المحلي النهائي لا يطابق دليل الطول/SHA-256 من موفر التسجيل.",
        ["Recoverable finalized capture is missing."] = "ملف التسجيل النهائي القابل للاسترداد غير موجود.",
        ["Recoverable capture no longer matches its finalized length/SHA-256 evidence."] = "ملف التسجيل القابل للاسترداد لم يعد يطابق دليل الطول/SHA-256 النهائي.",
        ["Authoritative Primary result does not match finalized capture evidence."] = "نتيجة التخزين الأساسي الموثوق لا تطابق دليل التسجيل النهائي.",
        ["Finalized capture ended before its declared length."] = "انتهى ملف التسجيل النهائي قبل الطول المعلن.",
        ["Capture is not finalized for durable upload handoff."] = "لم يتم اعتماد التسجيل للتسليم الدائم.",
        ["Capture handoff requires finalized length and SHA-256 evidence."] = "يتطلب تسليم التسجيل دليل الطول النهائي وSHA-256.",
        ["Starting"] = "جارٍ البدء",
        ["Recording"] = "جارٍ التسجيل",
        ["Stopping"] = "جارٍ الإيقاف",
        ["Finalizing"] = "جارٍ الاعتماد",
        ["ReadyForUpload"] = "جاهز للرفع",
        ["Failed"] = "فشل",
        ["TIMECODE"] = "التايم كود",
        ["Dropped frames"] = "الإطارات الساقطة",
        ["bytes"] = "بايت",
        ["upload"] = "رفع"
    };

    private static bool RegisterP07LocalizationLoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P07LocalizationWindowLoaded));
        return true;
    }

    private static void P07LocalizationWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP07RuntimeLocalization();
    }

    private void WireP07RuntimeLocalization()
    {
        if (_p07LocalizationWired) return;
        _p07LocalizationWired = true;
        RootGrid.LayoutUpdated += (_, _) => ApplyP07RuntimeArabicLocalization();
        LanguageButton.Click += (_, _) => Dispatcher.BeginInvoke(ApplyP07RuntimeArabicLocalization);
        ApplyP07RuntimeArabicLocalization();
    }

    // Called by acceptance through reflection as well, because the rendered audit uses an off-screen tree.
    private void ApplyP07RuntimeArabicLocalization()
    {
        if (_p07LocalizationApplying || !_arabic || !string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase) || ContentHost is null)
            return;

        _p07LocalizationApplying = true;
        try
        {
            foreach (var node in WalkP07Ui(ContentHost))
            {
                if (node is not TextBlock textBlock || string.IsNullOrWhiteSpace(textBlock.Text)) continue;
                var translated = TranslateP07Runtime(textBlock.Text);
                if (!string.Equals(translated, textBlock.Text, StringComparison.Ordinal)) textBlock.Text = translated;
            }
        }
        finally
        {
            _p07LocalizationApplying = false;
        }
    }

    private static string TranslateP07Runtime(string value)
    {
        if (P07RuntimeArabic.TryGetValue(value, out var exact)) return exact;

        var translated = value;
        foreach (var (english, arabic) in P07RuntimeArabic)
            if (english.Length > 5) translated = translated.Replace(english, arabic, StringComparison.Ordinal);

        translated = translated
            .Replace("BLOCK:", "منع:", StringComparison.Ordinal)
            .Replace("WARN:", "تحذير:", StringComparison.Ordinal)
            .Replace("Resume failed:", "فشل الاستكمال:", StringComparison.Ordinal)
            .Replace("Capture start failed:", "فشل بدء التسجيل:", StringComparison.Ordinal)
            .Replace("Status refresh failed:", "تعذر تحديث الحالة:", StringComparison.Ordinal)
            .Replace("Handoff stopped without deleting the temporary capture:", "توقف التسليم دون حذف ملف التسجيل المؤقت:", StringComparison.Ordinal)
            .Replace("Primary and Backup verified; temporary capture cache was safely cleaned.", "تم التحقق من التخزين الأساسي والاحتياطي وتنظيف ذاكرة التسجيل المؤقتة بأمان.", StringComparison.Ordinal)
            .Replace("Primary promoted for asset", "تم اعتماد التخزين الأساسي للأصل", StringComparison.Ordinal)
            .Replace("Protection:", "الحماية:", StringComparison.Ordinal)
            .Replace("Temporary capture remains until protection is verified.", "سيظل ملف التسجيل المؤقت حتى يتم التحقق من الحماية.", StringComparison.Ordinal)
            .Replace("Uploading to Primary", "جارٍ التسليم إلى التخزين الأساسي", StringComparison.Ordinal)
            .Replace(" · dropped ", " · إطارات ساقطة ", StringComparison.Ordinal)
            .Replace("Dropped frames:", "الإطارات الساقطة:", StringComparison.Ordinal)
            .Replace("TIMECODE ", "التايم كود ", StringComparison.Ordinal)
            .Replace(" bytes · ", " بايت · ", StringComparison.Ordinal)
            .Replace(" · upload ", " · رفع ", StringComparison.Ordinal);

        foreach (var state in new[] { "ReadyForUpload", "Finalizing", "Recording", "Starting", "Stopping", "Failed" })
            if (P07RuntimeArabic.TryGetValue(state, out var localized))
                translated = translated.Replace(state, localized, StringComparison.Ordinal);

        return translated;
    }

    private static IEnumerable<DependencyObject> WalkP07Ui(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;

            foreach (var child in LogicalTreeHelper.GetChildren(current))
                if (child is DependencyObject logical) stack.Push(logical);

            try
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                    stack.Push(VisualTreeHelper.GetChild(current, i));
            }
            catch (InvalidOperationException) { }
        }
    }
}
