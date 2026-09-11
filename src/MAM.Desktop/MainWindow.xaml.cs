using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MAM.Application.Branding;
using MAM.Application.Diagnostics;

namespace MAM.Desktop;

public partial class MainWindow : Window
{
    private bool _arabic;
    private string _currentRoute = "dashboard";

    private readonly Dictionary<string, (string En, string Ar)> _titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = ("Dashboard", "لوحة التحكم"),
        ["library"] = ("Media Library", "مكتبة الوسائط"),
        ["asset"] = ("Asset Details", "تفاصيل الأصل"),
        ["ingest"] = ("New Ingest", "إدخال جديد"),
        ["capture"] = ("Tape Capture", "التسجيل من الشريط"),
        ["upload"] = ("Upload", "رفع الملفات"),
        ["queue"] = ("Processing Queue", "قائمة المعالجة"),
        ["admin"] = ("Administration", "الإدارة"),
        ["settings"] = ("Settings", "الإعدادات")
    };

    public MainWindow()
    {
        InitializeComponent();
        LoadCrest(LoginCrest);
        LoadCrest(HeaderCrest);

        var build = BuildInfo.Current;
        BuildIdentityText.Text = $"{build.Version} · {build.EnvironmentName} · {build.CommitSha[..Math.Min(8, build.CommitSha.Length)]}";
        ApplyLanguage(false);
        ShowPage("dashboard");
    }

    private static void LoadCrest(Image target)
    {
        using var stream = new MemoryStream(DiwanCrestData.Bytes.ToArray());
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        target.Source = image;
    }

    private void EnterDemo_Click(object sender, RoutedEventArgs e)
    {
        LoginLayer.Visibility = Visibility.Collapsed;
        ShellLayer.Visibility = Visibility.Visible;
        ShowPage("dashboard");
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string route)
        {
            ShowPage(route);
        }
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        ApplyLanguage(!_arabic);
        ShowPage(_currentRoute);
    }

    private void ApplyLanguage(bool arabic)
    {
        _arabic = arabic;
        RootGrid.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageButton.Content = arabic ? "English" : "العربية";
        PageEyebrow.Text = arabic ? "الديوان الأميري · بيئة تطوير" : "DIWAN AL AMIRI · DEVELOPMENT";

        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (button.Tag is string route && _titles.TryGetValue(route, out var title))
            {
                button.Content = arabic ? title.Ar : title.En;
            }
        }
    }

    private void ShowPage(string route)
    {
        _currentRoute = _titles.ContainsKey(route) ? route : "dashboard";
        var title = _titles[_currentRoute];
        PageTitle.Text = _arabic ? title.Ar : title.En;

        ContentHost.Content = _currentRoute switch
        {
            "dashboard" => BuildDashboard(),
            "library" => BuildLibrary(),
            "asset" => BuildAssetDetails(),
            "ingest" => BuildIngest(),
            "capture" => BuildCapture(),
            "upload" => BuildUpload(),
            "queue" => BuildQueue(),
            "admin" => BuildAdmin(),
            "settings" => BuildSettings(),
            _ => BuildDashboard()
        };
    }

    private FrameworkElement BuildDashboard()
    {
        var body = PageStack(
            Lead(
                _arabic ? "نظرة تشغيلية واضحة للأرشيف" : "Operational view of the archive",
                _arabic
                    ? "بيانات العرض تجريبية ومعلّمة بوضوح. لا توجد ادعاءات بوجود ربط إنتاجي في P01."
                    : "Demo data is explicitly identified. P01 does not claim production backend connectivity."),
            MetricRow(
                Metric("1,248", _arabic ? "أصل تجريبي" : "Demo assets", "DEMO"),
                Metric("96.8%", _arabic ? "محمي تجريبيًا" : "Demo protected", "Primary + Backup verified"),
                Metric("14", _arabic ? "قيد المعالجة" : "Processing", "DEMO QUEUE"),
                Metric("2", _arabic ? "حالة متدهورة" : "Degraded services", "DEMO")),
            SectionTitle(_arabic ? "حالات واجهة النظام" : "System state treatments"),
            StateGrid()
        );
        return Scroll(body);
    }

    private FrameworkElement BuildLibrary()
    {
        var rows = new StackPanel();
        foreach (var item in new[]
        {
            ("DAA-2026-001248", "National ceremony master", "Video · 4K · 42:18", "Protected"),
            ("DAA-2026-001247", "Official reception gallery", "Images · 186 files", "Backup pending"),
            ("DAA-2026-001246", "Archive interview", "Video · HD · 18:09", "Processing"),
            ("DAA-2026-001245", "Protocol document", "PDF · 12 pages", "Protected")
        })
        {
            rows.Children.Add(ListRow(item.Item1, item.Item2, item.Item3, item.Item4));
        }

        return Scroll(PageStack(
            Lead(_arabic ? "مكتبة الوسائط" : "Media Library",
                 _arabic ? "شبكة مراجعة للأصول التجريبية مع حالات حماية واضحة." : "Reviewable demo catalog with explicit protection states."),
            Toolbar(_arabic ? "بحث، مرشحات، نوع الوسائط، الحالة" : "Search, filters, media type, protection status"),
            rows));
    }

    private FrameworkElement BuildAssetDetails()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "تفاصيل الأصل DAA-2026-001248" : "Asset Details · DAA-2026-001248",
                 _arabic ? "الصفحة تعرض بيانات تجريبية فقط." : "This page displays development/demo data only."),
            TwoColumn(
                Card(_arabic ? "معاينة" : "Preview",
                    new TextBlock { Text = _arabic ? "معاينة فيديو تجريبية\n00:18:42 / 00:42:18" : "Video preview shell\n00:18:42 / 00:42:18", FontSize = 22, Foreground = Navy() }),
                Card(_arabic ? "الحماية" : "Protection",
                    new TextBlock { Text = _arabic ? "Primary ✓\nBackup ✓\nSHA-256 مطابق ✓\nالحالة: Protected" : "Primary verified ✓\nBackup verified ✓\nSHA-256 match ✓\nState: Protected", FontSize = 15, Foreground = Text() })),
            Card(_arabic ? "البيانات الوصفية" : "Metadata",
                new TextBlock { Text = _arabic ? "العنوان · التاريخ · التصنيف · الوسوم · ملاحظات الحفظ" : "Title · date · category · tags · preservation notes", FontSize = 15, Foreground = Text() })));
    }

    private FrameworkElement BuildIngest()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "إدخال جديد" : "New Ingest",
                 _arabic ? "اختر مسار الإدخال المناسب. الأصول النهائية تذهب إلى الخادم المركزي." : "Choose an ingest path. Authoritative media is sent to the central server."),
            TwoColumn(
                ActionCard(_arabic ? "التسجيل من الشريط" : "Tape Capture",
                    _arabic ? "متاح على Windows فقط. مساحة العمل الاحترافية للأجهزة المعتمدة." : "Windows only. Professional workspace for approved capture hardware.", "capture"),
                ActionCard(_arabic ? "رفع ملفات" : "Upload Files",
                    _arabic ? "رفع من الجهاز مع فحص أولي قبل النقل للخادم." : "Select local files and run preflight before central upload.", "upload"))));
    }

    private FrameworkElement BuildCapture()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "مساحة التسجيل من الشريط" : "Windows Tape Capture Workspace",
                 _arabic ? "واجهة P01 فقط؛ ربط الأجهزة الفعلي في P07." : "P01 shell only; certified hardware integration is delivered in P07."),
            ThreeColumn(
                Card(_arabic ? "الجهاز" : "Device", new TextBlock { Text = _arabic ? "لا يوجد جهاز متصل · Demo" : "No capture device connected · Demo", Foreground = Text() }),
                Card(_arabic ? "المعاينة" : "Live Preview", new TextBlock { Text = "16:9 · TIMECODE 00:00:00:00", Foreground = Text() }),
                Card(_arabic ? "الصوت" : "Audio Meters", new TextBlock { Text = "CH1  ▰▰▰▰▱\nCH2  ▰▰▰▱▱", Foreground = Text() })),
            Card(_arabic ? "فحص ما قبل التسجيل" : "Capture preflight",
                new TextBlock { Text = _arabic ? "Temporary cache ✓  · الشبكة: غير متصلة (تجريبي) · مساحة القرص ✓" : "Temporary cache ✓ · Network: disconnected (demo) · Disk capacity ✓", Foreground = Text() })));
    }

    private FrameworkElement BuildUpload()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "رفع الملفات" : "Upload Workspace",
                 _arabic ? "المصدر المحلي مؤقت فقط؛ الملكية الدائمة تبقى على الخادم." : "Local selection/cache is temporary; authoritative storage remains server-side."),
            Card(_arabic ? "منطقة اختيار الملفات" : "File selection area",
                new TextBlock { Text = _arabic ? "اسحب الملفات هنا أو اخترها · Demo" : "Drop files here or browse · Demo", FontSize = 22, Foreground = Navy(), TextAlignment = TextAlignment.Center }),
            Card(_arabic ? "فحص أولي" : "Preflight",
                new TextBlock { Text = _arabic ? "الامتداد · الحجم · الاسم · المسار · جاهزية الشبكة" : "Extension · size · name · path · network readiness", Foreground = Text() })));
    }

    private FrameworkElement BuildQueue()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "قائمة المعالجة" : "Processing Queue",
                 _arabic ? "حالات تجريبية لإظهار النجاح والفشل وإعادة المحاولة." : "Demo states show success, failure and retry behavior."),
            ListRow("JOB-9031", "Proxy generation", "DAA-2026-001246", "Running 68%"),
            ListRow("JOB-9030", "Thumbnail generation", "DAA-2026-001245", "Completed"),
            ListRow("JOB-9029", "Technical metadata", "DAA-2026-001244", "Retry available"),
            ListRow("JOB-9028", "Backup verification", "DAA-2026-001243", "Degraded")));
    }

    private FrameworkElement BuildAdmin()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "الإدارة" : "Administration",
                 _arabic ? "واجهات تمهيدية؛ الصلاحيات والهوية المركزية في P02 والإدارة الكاملة في P08." : "Shell surfaces only; central identity/authorization arrives in P02 and full administration in P08."),
            MetricRow(
                Metric("24", _arabic ? "مستخدم" : "Users", "DEMO"),
                Metric("6", _arabic ? "أدوار" : "Roles", "DEMO"),
                Metric("3", _arabic ? "محطات التقاط" : "Capture stations", "DEMO"),
                Metric("2", _arabic ? "مخازن" : "Storage targets", "Primary / Backup")),
            StateCard(_arabic ? "رفض صلاحية" : "Permission denied", _arabic ? "هذا الإجراء يتطلب دور مدير النظام." : "This action requires the System Administrator role.", "#FEF3F2", "#B42318")));
    }

    private FrameworkElement BuildSettings()
    {
        return Scroll(PageStack(
            Lead(_arabic ? "الإعدادات" : "Settings",
                 _arabic ? "مرجع قابل للمراجعة لإعدادات البيئة؛ الأسرار لا تظهر في الواجهة." : "Reviewable environment settings shell; secrets are never displayed."),
            TwoColumn(
                Card(_arabic ? "اللغة والمظهر" : "Language & appearance",
                    new TextBlock { Text = _arabic ? "العربية / English\nNavy + Gold\nاتجاه RTL / LTR" : "English / العربية\nNavy + Gold\nLTR / RTL", Foreground = Text() }),
                Card(_arabic ? "التخزين" : "Storage",
                    new TextBlock { Text = _arabic ? "Primary: configured (demo)\nBackup: degraded (demo)\nSecrets: hidden" : "Primary: configured (demo)\nBackup: degraded (demo)\nSecrets: hidden", Foreground = Text() })),
            Card(_arabic ? "ملاحظة" : "Environment note",
                new TextBlock { Text = _arabic ? "تغييرات الإعدادات الفعلية تُدار لاحقًا بسياسات وصلاحيات مدققة." : "Production setting changes are delivered later with audited authorization and validation.", Foreground = Text() })));
    }

    private StackPanel StateGrid()
    {
        var stack = new StackPanel();
        stack.Children.Add(StateCard(_arabic ? "تحميل" : "Loading", _arabic ? "جاري تحميل بيانات العرض…" : "Loading demo catalog data…", "#EFF8FF", "#175CD3"));
        stack.Children.Add(StateCard(_arabic ? "لا توجد نتائج" : "Empty", _arabic ? "لا توجد أصول مطابقة للمرشحات." : "No assets match the current filters.", "#F9FAFB", "#475467"));
        stack.Children.Add(StateCard(_arabic ? "خطأ API" : "API error", _arabic ? "تعذر الوصول إلى الخدمة المركزية. أعد المحاولة." : "Central API is unreachable. Retry is available.", "#FEF3F2", "#B42318"));
        stack.Children.Add(StateCard(_arabic ? "رفض صلاحية" : "Permission denied", _arabic ? "ليست لديك صلاحية لهذا الإجراء." : "You do not have permission for this action.", "#FFF6ED", "#C4320A"));
        stack.Children.Add(StateCard(_arabic ? "خدمة متدهورة" : "Degraded", _arabic ? "Backup غير متاح؛ لا يتم اعتبار الأصل Protected." : "Backup is unavailable; assets are not marked Protected.", "#FFFAEB", "#B54708"));
        return stack;
    }

    private static Border StateCard(string title, string detail, string background, string foreground)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brush(foreground) });
        content.Children.Add(new TextBlock { Text = detail, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() });
        return new Border { Background = Brush(background), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8), Child = content };
    }

    private Border ActionCard(string title, string detail, string route)
    {
        var button = new Button
        {
            Content = _arabic ? "فتح المساحة" : "Open workspace",
            Tag = route,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Gold(),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += Navigate_Click;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock { Text = detail, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() });
        stack.Children.Add(button);
        return Card(string.Empty, stack);
    }

    private static FrameworkElement Toolbar(string text) =>
        new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            BorderBrush = Brush("#D0D5DD"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = new TextBlock { Text = "⌕  " + text, Foreground = Brush("#667085") }
        };

    private static Border ListRow(string id, string title, string meta, string state)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        AddCell(grid, id, 0, FontWeights.SemiBold);
        AddCell(grid, title, 1, FontWeights.SemiBold);
        AddCell(grid, meta, 2, FontWeights.Normal);
        AddCell(grid, state, 3, FontWeights.SemiBold, "#7A5512");
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#EAECF0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }

    private static void AddCell(Grid grid, string text, int column, FontWeight weight, string color = "#344054")
    {
        var block = new TextBlock { Text = text, FontWeight = weight, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static StackPanel PageStack(params UIElement[] children)
    {
        var stack = new StackPanel { MaxWidth = 1500 };
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }
        return stack;
    }

    private static StackPanel Lead(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock { Text = subtitle, Margin = new Thickness(0, 7, 0, 0), FontSize = 14, Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private static TextBlock SectionTitle(string text) =>
        new() { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Navy(), Margin = new Thickness(0, 18, 0, 12) };

    private static WrapPanel MetricRow(params UIElement[] children)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Border Metric(string value, string label, string note)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Text() });
        stack.Children.Add(new TextBlock { Text = note, Margin = new Thickness(0, 4, 0, 0), FontSize = 11, Foreground = Brush("#99731F") });
        return new Border { Width = 235, MinHeight = 132, Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 0, 12, 12), BorderBrush = Brush("#EAECF0"), BorderThickness = new Thickness(1), Child = stack };
    }

    private static Grid TwoColumn(UIElement left, UIElement right) => Columns(new[] { left, right });
    private static Grid ThreeColumn(UIElement one, UIElement two, UIElement three) => Columns(new[] { one, two, three });

    private static Grid Columns(IReadOnlyList<UIElement> children)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < children.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(children[i], i);
            if (children[i] is FrameworkElement element)
            {
                element.Margin = new Thickness(i == 0 ? 0 : 8, 0, i == children.Count - 1 ? 0 : 8, 0);
            }
            grid.Children.Add(children[i]);
        }
        return grid;
    }

    private static Border Card(string title, UIElement content)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Navy(), Margin = new Thickness(0, 0, 0, 12) });
        }
        stack.Children.Add(content);
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), BorderBrush = Brush("#EAECF0"), BorderThickness = new Thickness(1), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static SolidColorBrush Navy() => Brush("#0A2342");
    private static SolidColorBrush Gold() => Brush("#B58A2A");
    private static SolidColorBrush Text() => Brush("#344054");

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is null) return;
        SidebarColumn.Width = new GridLength(ActualWidth < 1180 ? 188 : 232);
    }
}
