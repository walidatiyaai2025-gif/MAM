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
        ["tapes"] = ("Tape Inventory", "إدارة الشرائط"),
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
        var sha = build.CommitSha.Length > 8 ? build.CommitSha[..8] : build.CommitSha;
        BuildIdentityText.Text = $"{build.Version} · {DesktopProductionTransport.EnvironmentLabel} · {sha}";
        LoginLayer.Visibility = DesktopProductionTransport.IsProduction ? Visibility.Visible : Visibility.Collapsed;
        ShellLayer.Visibility = DesktopProductionTransport.IsProduction ? Visibility.Collapsed : Visibility.Visible;
        DesktopProductionTransport.SessionInvalidated += OnProductionSessionInvalidated;
        Closed += (_, _) => DesktopProductionTransport.SessionInvalidated -= OnProductionSessionInvalidated;
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

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string route) ShowPage(route);
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
        PageEyebrow.Text = arabic ? "الديوان الأميري · الإنتاج" : "DIWAN AL AMIRI · PRODUCTION";
        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (button.Tag is string route && _titles.TryGetValue(route, out var title))
                button.Content = arabic ? title.Ar : title.En;
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
            "tapes" => BuildTapeInventoryPlaceholder(),
            "upload" => BuildUpload(),
            "queue" => BuildQueue(),
            "admin" => BuildAdmin(),
            "settings" => BuildSettings(),
            _ => BuildDashboard()
        };
    }

    private FrameworkElement BuildDashboard() => Scroll(PageStack(
        Lead(_arabic ? "نظرة تشغيلية للأرشيف" : "Archive operational overview",
             _arabic ? "يتم تحميل مؤشرات الإنتاج من الخدمة المركزية." : "Loading live Production metrics from the authoritative service."),
        StateCard("Production",
            _arabic
                ? $"متصل بـ mam.da.gov.kw باستخدام {DesktopProductionTransport.AuthenticationLabel}."
                : $"Connected to mam.da.gov.kw using {DesktopProductionTransport.AuthenticationLabel}.",
            "#ECFDF3", "#027A48")));

    private FrameworkElement BuildLibrary() => Scroll(PageStack(
        Lead(_arabic ? "مكتبة الوسائط" : "Media Library",
            _arabic ? "يتم تحميل الكتالوج المركزي للإنتاج." : "Loading the authoritative Production catalog."),
        StateCard("Loading",
            _arabic ? "جاري تحميل الأصول من الخدمة المركزية…" : "Loading assets from the Central API…",
            "#EFF8FF", "#175CD3")));

    private FrameworkElement BuildAssetDetails() => Scroll(PageStack(
        Lead(_arabic ? "تفاصيل الأصل" : "Asset Details",
            _arabic ? "اختر أصلًا من مكتبة الإنتاج لعرض بياناته الفنية." : "Select an asset from the Production Media Library to inspect authoritative details."),
        StateCard("Production",
            _arabic ? "لا توجد بيانات تجريبية محلية في هذه الصفحة." : "No local demo data is used on this page.",
            "#ECFDF3", "#027A48")));

    private FrameworkElement BuildIngest() => Scroll(PageStack(
        Lead(_arabic ? "إدخال جديد" : "New Ingest", _arabic ? "اختر مسار الإدخال في بيئة الإنتاج." : "Choose a Production ingest path."),
        TwoColumn(
            ActionCard(_arabic ? "إدارة الشرائط" : "Tape Inventory",
                _arabic ? "إدارة سجلات الشرائط المادية فقط؛ لا يوجد تسجيل مباشر من الشريط داخل MAM." : "Manage physical tape records only; direct tape recording is not part of MAM.",
                "tapes"),
            ActionCard(_arabic ? "رفع ملفات" : "Upload Files",
                _arabic ? "رفع الملفات أو المجلدات إلى التخزين المركزي للإنتاج." : "Upload files or folders to authoritative Production storage.",
                "upload"))));

    private FrameworkElement BuildTapeInventoryPlaceholder() => Scroll(PageStack(
        Lead(_arabic ? "إدارة الشرائط" : "Tape Inventory",
            _arabic ? "إدارة مركزية لسجلات الشرائط المادية." : "Authoritative Production tape inventory."),
        StateCard("Loading",
            _arabic ? "جاري تحميل إدارة الشرائط المركزية…" : "Loading authoritative tape management…",
            "#EFF8FF", "#175CD3")));

    private FrameworkElement BuildUpload() => Scroll(PageStack(
        Lead(_arabic ? "رفع الملفات" : "Upload Workspace",
            _arabic ? "التخزين المحلي مؤقت؛ الحفظ النهائي على خادم الإنتاج." : "Local cache is temporary; authoritative storage remains on the Production server."),
        StateCard("Loading",
            _arabic ? "جاري تهيئة رفع الملفات إلى الخدمة المركزية…" : "Initializing Central API upload workspace…",
            "#EFF8FF", "#175CD3")));

    private FrameworkElement BuildQueue() => Scroll(PageStack(
        Lead(_arabic ? "قائمة المعالجة" : "Processing Queue",
            _arabic ? "الحالة المباشرة لقائمة المعالجة المركزية." : "Live state from the authoritative Production processing queue."),
        StateCard("Loading",
            _arabic ? "جاري تحميل وظائف المعالجة…" : "Loading Production processing jobs…",
            "#EFF8FF", "#175CD3")));

    private FrameworkElement BuildAdmin() => Scroll(PageStack(
        Lead(_arabic ? "الإدارة" : "Administration",
            _arabic ? "إدارة المستخدمين والسياسات من الخدمة المركزية." : "Authoritative users, roles and policy administration."),
        StateCard("Loading",
            _arabic ? "جاري تحميل صلاحيات وإعدادات الإنتاج…" : "Loading Production permissions and settings…",
            "#EFF8FF", "#175CD3")));

    private FrameworkElement BuildSettings() => Scroll(PageStack(
        Lead(_arabic ? "الإعدادات" : "Settings",
            _arabic ? "إعدادات تطبيق الإنتاج. القيم السرية لا يتم عرضها." : "Production Desktop settings. Secret values are never displayed."),
        TwoColumn(
            Card(_arabic ? "الاتصال" : "Connection", new TextBlock
            {
                Text = $"{DesktopProductionTransport.ProductionOrigin}\n{DesktopProductionTransport.AuthenticationLabel}\nDomain join optional\nProduction",
                Foreground = Text(),
                TextWrapping = TextWrapping.Wrap
            }),
            Card(_arabic ? "اللغة والمظهر" : "Language & appearance", new TextBlock
            {
                Text = "English / العربية\nNavy + Gold\nLTR / RTL",
                Foreground = Text()
            }))));

    private StackPanel StateGrid()
    {
        var stack = new StackPanel();
        stack.Children.Add(StateCard("Loading", "Loading Production service data…", "#EFF8FF", "#175CD3"));
        stack.Children.Add(StateCard("Empty", "No authoritative assets match the current filters.", "#F9FAFB", "#475467"));
        stack.Children.Add(StateCard("API error", "Production Central API is unreachable. Retry is available.", "#FEF3F2", "#B42318"));
        stack.Children.Add(StateCard("Permission denied", "Your Production MAM role does not permit this action.", "#FFF6ED", "#C4320A"));
        stack.Children.Add(StateCard("Degraded", "A Production dependency is unavailable; no local fallback is used.", "#FFFAEB", "#B54708"));
        return stack;
    }

    private static Border StateCard(string title, string detail, string background, string foreground)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Brush(foreground) });
        content.Children.Add(new TextBlock { Text = detail, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() });
        return Card(string.Empty, content, Brush(background));
    }

    private Border ActionCard(string title, string detail, string route)
    {
        var button = new Button
        {
            Content = _arabic ? "فتح المساحة" : "Open workspace",
            Tag = route,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14, 9, 14, 9),
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

    private static FrameworkElement Toolbar(string text) => Card(string.Empty, new TextBlock { Text = "⌕  " + text, Foreground = Brush("#667085") });

    private static Border ListRow(string id, string title, string meta, string state)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        AddCell(grid, id, 0, FontWeights.SemiBold);
        AddCell(grid, title, 1, FontWeights.SemiBold);
        AddCell(grid, meta, 2, FontWeights.Normal);
        AddCell(grid, state, 3, FontWeights.SemiBold);
        return Card(string.Empty, grid);
    }

    private static void AddCell(Grid grid, string text, int column, FontWeight weight)
    {
        var block = new TextBlock { Text = text, FontWeight = weight, Foreground = Text(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static StackPanel PageStack(params UIElement[] children)
    {
        var stack = new StackPanel { MaxWidth = 1500 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }

    private static StackPanel Lead(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock { Text = subtitle, Margin = new Thickness(0, 7, 0, 0), Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Navy(), Margin = new Thickness(0, 18, 0, 12) };

    private static WrapPanel MetricRow(params UIElement[] children)
    {
        var panel = new WrapPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Border Metric(string value, string label, string note)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Text() });
        stack.Children.Add(new TextBlock { Text = note, Margin = new Thickness(0, 4, 0, 0), FontSize = 11, Foreground = Brush("#99731F") });
        return Card(string.Empty, stack);
    }

    private static Grid TwoColumn(UIElement left, UIElement right) => Columns(left, right);
    private static Grid ThreeColumn(UIElement one, UIElement two, UIElement three) => Columns(one, two, three);

    private static Grid Columns(params UIElement[] children)
    {
        var grid = new Grid();
        for (var i = 0; i < children.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(children[i], i);
            if (children[i] is FrameworkElement element)
                element.Margin = new Thickness(i == 0 ? 0 : 8, 0, i == children.Length - 1 ? 0 : 8, 0);
            grid.Children.Add(children[i]);
        }
        return grid;
    }

    private static Border Card(string title, UIElement content, Brush? background = null)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
            stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Navy(), Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(content);
        return new Border
        {
            Background = background ?? Brushes.White,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Brush("#EAECF0"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static SolidColorBrush Navy() => Brush("#0A2342");
    private static SolidColorBrush Gold() => Brush("#B58A2A");
    private static SolidColorBrush Text() => Brush("#344054");

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is not null) SidebarColumn.Width = new GridLength(ActualWidth < 1180 ? 188 : 232);
    }
}
