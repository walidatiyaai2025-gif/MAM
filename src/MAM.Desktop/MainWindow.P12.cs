using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Clients;
using MAM.Application.Discovery;
using MAM.Application.Processing;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamDiscoveryApiClient? _p12DiscoveryClient;
    private HttpClient? _p12HttpClient;
    private IReadOnlyList<CategorySnapshot> _p12Categories = Array.Empty<CategorySnapshot>();

    private void InitializeP12DiscoveryIntegration()
    {
        if (_p12DiscoveryClient is not null) return;
        _p12HttpClient = DesktopProductionTransport.CreateApiClient(TimeSpan.FromMinutes(5));
        if (_p12HttpClient is null) return;
        _p12DiscoveryClient = new MamDiscoveryApiClient(
            _p12HttpClient,
            "WindowsDesktop",
            DesktopProductionTransport.DevelopmentUser);

        _titles["search"] = ("Content Search", "البحث في المحتوى");
        _titles["categories"] = ("Categories", "التصنيفات");
        _titles["collections"] = ("Collections", "المجموعات");
        _titles["tags"] = ("Tags", "الوسوم");
        _titles["references"] = ("Reference Library", "مكتبة المراجع");
        _titles["mediaPermissions"] = ("Media Permissions", "صلاحيات أنواع الوسائط");

        AddP12NavButton("search");
        AddP12NavButton("categories");
        AddP12NavButton("collections");
        AddP12NavButton("tags");
        AddP12NavButton("references");
        AddP12NavButton("mediaPermissions");

        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (string.Equals(button.Tag as string, "dashboard", StringComparison.OrdinalIgnoreCase))
                button.Click += P12DashboardNavigate_Click;
        }
        LanguageButton.Click += P12LanguageChanged_Click;
        if (IsP12Route(_currentRoute)) _ = LoadP12RouteAsync(_currentRoute);
        else if (string.Equals(_currentRoute, "dashboard", StringComparison.OrdinalIgnoreCase)) _ = AugmentP12DashboardAsync();
    }

    private void AddP12NavButton(string route)
    {
        if (NavPanel.Children.OfType<Button>().Any(b => string.Equals(b.Tag as string, route, StringComparison.OrdinalIgnoreCase))) return;
        var title = _titles[route];
        var button = new Button
        {
            Tag = route,
            Content = _arabic ? title.Ar : title.En,
            Style = (Style)FindResource("NavButton")
        };
        button.Click += async (_, _) =>
        {
            _currentRoute = route;
            PageTitle.Text = _arabic ? title.Ar : title.En;
            await LoadP12RouteAsync(route);
        };
        NavPanel.Children.Add(button);
    }

    private async void P12DashboardNavigate_Click(object sender, RoutedEventArgs e) => await AugmentP12DashboardAsync();

    private async void P12LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (button.Tag is string route && _titles.TryGetValue(route, out var title))
                button.Content = _arabic ? title.Ar : title.En;
        }
        if (IsP12Route(_currentRoute)) await LoadP12RouteAsync(_currentRoute);
        else if (string.Equals(_currentRoute, "dashboard", StringComparison.OrdinalIgnoreCase)) await AugmentP12DashboardAsync();
    }

    private static bool IsP12Route(string route) =>
        route is "search" or "categories" or "collections" or "tags" or "references" or "mediaPermissions";

    private Task LoadP12RouteAsync(string route) => route switch
    {
        "search" => LoadP12SearchAsync(),
        "categories" => LoadP12CategoriesAsync(),
        "collections" => LoadManagementCollectionsAsync(),
        "tags" => LoadManagementTagsAsync(),
        "references" => LoadP12ReferencesAsync(),
        "mediaPermissions" => LoadP12MediaPermissionsAsync(),
        _ => Task.CompletedTask
    };

    private async Task AugmentP12DashboardAsync()
    {
        if (_p12DiscoveryClient is null || !string.Equals(_currentRoute, "dashboard", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var metrics = await _p12DiscoveryClient.GetDashboardAsync();
            if (!string.Equals(_currentRoute, "dashboard", StringComparison.OrdinalIgnoreCase)) return;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "نظرة تشغيلية للأرشيف والفهرس" : "Archive and discovery overview",
                    _arabic ? "مؤشرات مباشرة من الفهرس المركزي." : "Live metrics from the authoritative discovery index."),
                MetricRow(
                    Metric(metrics.CategoryCount.ToString("N0"), _arabic ? "التصنيفات" : "Categories", $"{metrics.UncategorizedAssetCount:N0} {(_arabic ? "غير مصنف" : "uncategorized")}"),
                    Metric(metrics.IndexedAssetCount.ToString("N0"), _arabic ? "أصول مفهرسة" : "Indexed assets", $"{metrics.TranscriptCount:N0} transcripts"),
                    Metric(metrics.OcrCount.ToString("N0"), "OCR", $"{metrics.ReferenceSubjectCount:N0} {(_arabic ? "مرجع" : "references")}")),
                StateCard("Central API", _arabic ? "البحث والتصنيفات والنصوص المستخرجة تحت سلطة SQL والخدمة المركزية." : "Search, categories and extracted text remain SQL/Central-API authoritative.", "#ECFDF3", "#027A48")));
        }
        catch { }
    }

    private async Task LoadP12SearchAsync()
    {
        if (_p12DiscoveryClient is null) return;
        ShowP12Loading("search", _arabic ? "البحث في المحتوى" : "Content Search", _arabic ? "جاري تحميل خيارات البحث…" : "Loading discovery filters…");
        try
        {
            _p12Categories = await _p12DiscoveryClient.ListCategoriesAsync();
            if (!string.Equals(_currentRoute, "search", StringComparison.OrdinalIgnoreCase)) return;

            var query = new TextBox { MinWidth = 360, MaxLength = 300, Padding = new Thickness(10, 8, 10, 8), ToolTip = _arabic ? "نص البحث" : "Search text" };
            var mediaKind = new ComboBox { MinWidth = 150, Margin = new Thickness(8, 0, 0, 0) };
            mediaKind.Items.Add(new ComboBoxItem { Content = _arabic ? "كل أنواع الوسائط" : "All media types", Tag = string.Empty, IsSelected = true });
            foreach (var kind in new[] { MediaKinds.Video, MediaKinds.Audio, MediaKinds.Image, MediaKinds.Document, MediaKinds.Other })
                mediaKind.Items.Add(new ComboBoxItem { Content = kind, Tag = kind });

            var category = new ComboBox { MinWidth = 220, Margin = new Thickness(8, 0, 0, 0) };
            category.Items.Add(new ComboBoxItem { Content = _arabic ? "كل التصنيفات" : "All categories", Tag = null, IsSelected = true });
            foreach (var item in _p12Categories)
                category.Items.Add(new ComboBoxItem { Content = P12CategoryDisplay(item), Tag = item.CategoryId });

            var results = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var search = P04ActionButton(_arabic ? "بحث" : "Search");
            search.Margin = new Thickness(8, 0, 0, 0);
            async Task ExecuteAsync()
            {
                if (query.Text.Trim().Length < 2)
                {
                    results.Children.Clear();
                    results.Children.Add(StateCard("Validation", _arabic ? "أدخل حرفين على الأقل." : "Enter at least two searchable characters.", "#FFF6ED", "#C4320A"));
                    return;
                }
                search.IsEnabled = false;
                results.Children.Clear();
                results.Children.Add(StateCard("Loading", _arabic ? "جاري البحث داخل العنوان والبيانات وOCR والتفريغ…" : "Searching title, metadata, OCR and transcript indexes…", "#EFF8FF", "#175CD3"));
                try
                {
                    var categoryId = category.SelectedItem is ComboBoxItem categoryItem && categoryItem.Tag is Guid id ? id : (Guid?)null;
                    var kind = mediaKind.SelectedItem is ComboBoxItem kindItem ? kindItem.Tag as string : null;
                    var response = await _p12DiscoveryClient.SearchAsync(new DiscoverySearchRequest(query.Text.Trim(), 1, 100, categoryId, kind));
                    results.Children.Clear();
                    if (response.Items.Count == 0)
                    {
                        results.Children.Add(StateCard("Empty", _arabic ? "لا توجد وسائط مرتبطة بالنص المطلوب." : "No related media was found.", "#F9FAFB", "#475467"));
                        return;
                    }
                    foreach (var hit in response.Items)
                    {
                        var detail = hit.StartMs is not null
                            ? $"{P12Time(hit.StartMs)} → {P12Time(hit.EndMs)}"
                            : hit.PageNumber is not null ? $"{(_arabic ? "صفحة" : "Page")} {hit.PageNumber}" : hit.MatchedSource;
                        var tags = hit.ReferenceTags.Count == 0 ? string.Empty : $"\n{(_arabic ? "وسوم" : "Tags")}: {string.Join(" · ", hit.ReferenceTags)}";
                        results.Children.Add(ListRow(hit.MediaKind, hit.Title, $"{P12CategoryName(hit.CategoryNameEn, hit.CategoryNameAr)}\n{hit.Snippet}{tags}", detail));
                    }
                }
                catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    results.Children.Clear(); results.Children.Add(StateCard("Permission denied", _arabic ? "لا توجد صلاحية للبحث في هذه الوسائط." : "Permission denied for discovery search.", "#FFF6ED", "#C4320A"));
                }
                catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
                {
                    results.Children.Clear(); results.Children.Add(StateCard("API error", _arabic ? "فشل البحث عبر الخدمة المركزية." : "Discovery search failed through the Central API.", "#FEF3F2", "#B42318"));
                }
                finally { search.IsEnabled = true; }
            }
            search.Click += async (_, _) => await ExecuteAsync();
            query.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await ExecuteAsync(); };

            var toolbar = new WrapPanel();
            toolbar.Children.Add(query); toolbar.Children.Add(mediaKind); toolbar.Children.Add(category); toolbar.Children.Add(search);
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "البحث في المحتوى" : "Content Search", _arabic ? "بحث مركزي في البيانات وOCR والتفريغ والوسوم المرجعية." : "Central search across metadata, OCR, timestamped transcripts and reference tags."),
                Card(_arabic ? "بحث نصي شامل" : "Full-text discovery", toolbar), results));
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException) { ShowP12Error("search"); }
    }

    private async Task LoadP12CategoriesAsync()
    {
        if (_p12DiscoveryClient is null) return;
        ShowP12Loading("categories", _arabic ? "التصنيفات" : "Categories", _arabic ? "جاري تحميل شجرة التصنيفات…" : "Loading category hierarchy…");
        try
        {
            _p12Categories = await _p12DiscoveryClient.ListCategoriesAsync();
            if (!string.Equals(_currentRoute, "categories", StringComparison.OrdinalIgnoreCase)) return;

            var nameEn = new TextBox { MinWidth = 220, MaxLength = 200, Padding = new Thickness(8), ToolTip = "Category name (English)" };
            var nameAr = new TextBox { MinWidth = 220, MaxLength = 200, Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0), ToolTip = "اسم التصنيف بالعربية", FlowDirection = FlowDirection.RightToLeft };
            var parent = new ComboBox { MinWidth = 240, Margin = new Thickness(8, 0, 0, 0) };
            parent.Items.Add(new ComboBoxItem { Content = _arabic ? "بدون أب" : "No parent — root", Tag = null, IsSelected = true });
            foreach (var item in _p12Categories.Where(c => !c.IsSystem)) parent.Items.Add(new ComboBoxItem { Content = P12CategoryDisplay(item), Tag = item.CategoryId });
            var sort = new TextBox { Width = 70, Text = "0", Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0), ToolTip = _arabic ? "الترتيب" : "Sort order" };
            var state = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            var save = P04ActionButton(_arabic ? "إضافة تصنيف" : "Add category");
            save.Margin = new Thickness(8, 0, 0, 0);
            save.Click += async (_, _) =>
            {
                if (nameEn.Text.Trim().Length == 0) { state.Text = _arabic ? "الاسم الإنجليزي مطلوب." : "English category name is required."; return; }
                var parentId = parent.SelectedItem is ComboBoxItem item && item.Tag is Guid id ? id : (Guid?)null;
                _ = int.TryParse(sort.Text, out var sortOrder);
                save.IsEnabled = false;
                try
                {
                    await _p12DiscoveryClient.CreateCategoryAsync(new CreateCategoryRequest(parentId, nameEn.Text.Trim(), string.IsNullOrWhiteSpace(nameAr.Text) ? null : nameAr.Text.Trim(), sortOrder));
                    await LoadP12CategoriesAsync();
                }
                catch (MamApiException ex) { state.Text = ex.Message; }
                finally { save.IsEnabled = true; }
            };
            var editor = new WrapPanel(); editor.Children.Add(nameEn); editor.Children.Add(nameAr); editor.Children.Add(parent); editor.Children.Add(sort); editor.Children.Add(save);

            var list = new StackPanel();
            foreach (var item in _p12Categories)
            {
                var row = new StackPanel();
                row.Children.Add(new TextBlock { Text = P12CategoryDisplay(item), FontWeight = FontWeights.Bold, Foreground = Navy() });
                row.Children.Add(new TextBlock { Text = $"{item.AssetCount:N0} {(_arabic ? "أصل" : "assets")} · {item.ChildCount:N0} {(_arabic ? "فرعي" : "children")} · v{item.Version}", Foreground = Text(), Margin = new Thickness(0, 4, 0, 0) });
                if (!item.IsSystem)
                {
                    var buttons = new WrapPanel();
                    var rename = P04ActionButton(_arabic ? "تعديل" : "Edit");
                    rename.Click += async (_, _) =>
                    {
                        var dialog = new P12CategoryEditDialog(item, _p12Categories, _arabic) { Owner = this };
                        if (dialog.ShowDialog() != true) return;
                        try
                        {
                            await _p12DiscoveryClient.UpdateCategoryAsync(item.CategoryId, new UpdateCategoryRequest(item.Version, dialog.ParentCategoryId, dialog.NameEn, dialog.NameAr, dialog.SortOrder));
                            await LoadP12CategoriesAsync();
                        }
                        catch (MamApiException ex) { MessageBox.Show(this, ex.Message, _arabic ? "تعذر الحفظ" : "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    };
                    var delete = P04ActionButton(_arabic ? "حذف" : "Delete");
                    delete.Background = Brush("#B42318");
                    delete.Click += async (_, _) =>
                    {
                        try { await _p12DiscoveryClient.DeleteCategoryAsync(item.CategoryId); await LoadP12CategoriesAsync(); }
                        catch (MamApiException ex) { MessageBox.Show(this, ex.Message, _arabic ? "تعذر الحذف" : "Delete failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    };
                    buttons.Children.Add(rename); buttons.Children.Add(delete); row.Children.Add(buttons);
                }
                list.Children.Add(Card(string.Empty, row));
            }
            var createStack = new StackPanel(); createStack.Children.Add(editor); createStack.Children.Add(state);
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إدارة التصنيفات" : "Category Management", _arabic ? "هيكل أب/فرع اختياري مع غير مصنف تلقائيًا." : "Optional parent/child hierarchy with automatic Uncategorized fallback."),
                Card(_arabic ? "إضافة تصنيف" : "Create category", createStack), list));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { ShowP12Denied("categories"); }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException) { ShowP12Error("categories"); }
    }

    private async Task LoadP12ReferencesAsync()
    {
        if (_p12DiscoveryClient is null) return;
        ShowP12Loading("references", _arabic ? "مكتبة المراجع" : "Reference Library", _arabic ? "جاري تحميل المراجع…" : "Loading reference library…");
        try
        {
            var references = await _p12DiscoveryClient.ListReferenceSubjectsAsync();
            if (!string.Equals(_currentRoute, "references", StringComparison.OrdinalIgnoreCase)) return;
            var nameEn = new TextBox { MinWidth = 220, Padding = new Thickness(8), MaxLength = 200, ToolTip = "Name (English)" };
            var nameAr = new TextBox { MinWidth = 220, Padding = new Thickness(8), MaxLength = 200, Margin = new Thickness(8, 0, 0, 0), ToolTip = "الاسم بالعربية", FlowDirection = FlowDirection.RightToLeft };
            var tags = new TextBox { MinWidth = 260, Padding = new Thickness(8), MaxLength = 1000, Margin = new Thickness(8, 0, 0, 0), ToolTip = _arabic ? "وسوم مفصولة بفواصل" : "Comma-separated tags" };
            var create = P04ActionButton(_arabic ? "إضافة مرجع" : "Add reference"); create.Margin = new Thickness(8, 0, 0, 0);
            var createState = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            create.Click += async (_, _) =>
            {
                if (nameEn.Text.Trim().Length == 0) { createState.Text = _arabic ? "الاسم الإنجليزي مطلوب." : "English name is required."; return; }
                try
                {
                    await _p12DiscoveryClient.CreateReferenceSubjectAsync(new CreateReferenceSubjectRequest(nameEn.Text.Trim(), string.IsNullOrWhiteSpace(nameAr.Text) ? null : nameAr.Text.Trim(), null, null, tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    await LoadP12ReferencesAsync();
                }
                catch (MamApiException ex) { createState.Text = ex.Message; }
            };
            var form = new WrapPanel(); form.Children.Add(nameEn); form.Children.Add(nameAr); form.Children.Add(tags); form.Children.Add(create);
            var formStack = new StackPanel(); formStack.Children.Add(form); formStack.Children.Add(createState);
            var cards = new StackPanel();
            foreach (var reference in references)
            {
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = P12CategoryName(reference.NameEn, reference.NameAr), FontWeight = FontWeights.Bold, FontSize = 18, Foreground = Navy() });
                stack.Children.Add(new TextBlock { Text = string.Join(" · ", reference.Tags), Foreground = Text(), Margin = new Thickness(0, 4, 0, 0) });
                stack.Children.Add(new TextBlock { Text = $"{(_arabic ? "صور مرجعية" : "Reference images")}: {reference.ReferenceAssetIds.Count}", Foreground = Text(), Margin = new Thickness(0, 4, 0, 0) });
                var assetId = new TextBox { MinWidth = 300, Padding = new Thickness(8), Margin = new Thickness(0, 8, 8, 0), ToolTip = _arabic ? "معرف أصل صورة" : "Image Asset ID" };
                var attach = P04ActionButton(_arabic ? "ربط صورة" : "Attach image");
                var attachState = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Text(), Margin = new Thickness(0, 4, 0, 0) };
                attach.Click += async (_, _) =>
                {
                    if (!Guid.TryParse(assetId.Text.Trim(), out var id)) { attachState.Text = _arabic ? "معرف الأصل غير صالح." : "Invalid asset ID."; return; }
                    try { await _p12DiscoveryClient.AddReferenceImageAsync(reference.SubjectId, id); await LoadP12ReferencesAsync(); }
                    catch (MamApiException ex) { attachState.Text = ex.Message; }
                };
                var attachRow = new WrapPanel(); attachRow.Children.Add(assetId); attachRow.Children.Add(attach); stack.Children.Add(attachRow); stack.Children.Add(attachState);
                cards.Children.Add(Card(string.Empty, stack));
            }
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "مكتبة المراجع" : "Reference Library", _arabic ? "مراجع وصور مرجعية ووسوم قابلة للبحث؛ لا يتم التعرف التلقائي على هوية الأشخاص." : "Searchable reference subjects, images and tags; automatic person-identity recognition is not performed."),
                Card(_arabic ? "إضافة مرجع" : "Add reference subject", formStack), cards));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { ShowP12Denied("references"); }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException) { ShowP12Error("references"); }
    }

    private async Task LoadP12MediaPermissionsAsync()
    {
        if (_p12DiscoveryClient is null) return;
        ShowP12Loading("mediaPermissions", _arabic ? "صلاحيات أنواع الوسائط" : "Media Type Permissions", _arabic ? "جاري تحميل مصفوفة الصلاحيات…" : "Loading media permission matrix…");
        try
        {
            var rows = await _p12DiscoveryClient.ListMediaPermissionsAsync();
            if (!string.Equals(_currentRoute, "mediaPermissions", StringComparison.OrdinalIgnoreCase)) return;
            var list = new StackPanel();
            foreach (var permission in rows)
            {
                var view = new CheckBox { Content = _arabic ? "عرض" : "View", IsChecked = permission.CanView, Margin = new Thickness(0, 0, 10, 0) };
                var upload = new CheckBox { Content = _arabic ? "رفع" : "Upload", IsChecked = permission.CanUpload, Margin = new Thickness(0, 0, 10, 0) };
                var edit = new CheckBox { Content = _arabic ? "تعديل" : "Edit", IsChecked = permission.CanEdit, Margin = new Thickness(0, 0, 10, 0) };
                var process = new CheckBox { Content = _arabic ? "معالجة" : "Process", IsChecked = permission.CanProcess, Margin = new Thickness(0, 0, 10, 0) };
                var download = new CheckBox { Content = _arabic ? "تنزيل" : "Download", IsChecked = permission.CanDownload, Margin = new Thickness(0, 0, 10, 0) };
                var save = P04ActionButton(_arabic ? "حفظ" : "Save");
                var state = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
                save.Click += async (_, _) =>
                {
                    save.IsEnabled = false;
                    try
                    {
                        await _p12DiscoveryClient.UpsertMediaPermissionAsync(new UpsertMediaPermissionRequest(permission.RoleName, permission.MediaKind, view.IsChecked == true, upload.IsChecked == true, edit.IsChecked == true, process.IsChecked == true, download.IsChecked == true));
                        state.Text = _arabic ? "تم الحفظ." : "Saved.";
                    }
                    catch (MamApiException ex) { state.Text = ex.Message; }
                    finally { save.IsEnabled = true; }
                };
                var checks = new WrapPanel(); checks.Children.Add(view); checks.Children.Add(upload); checks.Children.Add(edit); checks.Children.Add(process); checks.Children.Add(download); checks.Children.Add(save);
                var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = $"{permission.RoleName} · {permission.MediaKind}", FontWeight = FontWeights.Bold, Foreground = Navy() }); stack.Children.Add(checks); stack.Children.Add(state);
                list.Children.Add(Card(string.Empty, stack));
            }
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "صلاحيات أنواع الوسائط" : "Media Type Permissions", _arabic ? "التحكم الخادمي في العرض والرفع والتعديل والمعالجة والتنزيل." : "Server-enforced view/upload/edit/process/download permissions per role and media kind."), list));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { ShowP12Denied("mediaPermissions"); }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException) { ShowP12Error("mediaPermissions"); }
    }

    internal async Task<FrameworkElement> BuildP12AssetDiscoveryPanelAsync(Guid assetId, string? mediaType)
    {
        if (_p12DiscoveryClient is null) return StateCard("Degraded", _arabic ? "خدمة الاكتشاف غير مهيأة." : "Discovery service is not configured.", "#FFFAEB", "#B54708");
        try
        {
            var categoriesTask = _p12DiscoveryClient.ListCategoriesAsync();
            var categoryTask = _p12DiscoveryClient.GetAssetCategoryAsync(assetId);
            var statusTask = _p12DiscoveryClient.GetExtractionStatusAsync(assetId);
            var tagsTask = _p12DiscoveryClient.ListAssetReferenceTagsAsync(assetId);
            var refsTask = _p12DiscoveryClient.ListReferenceSubjectsAsync();
            var transcriptTask = _p12DiscoveryClient.GetTextAsync(assetId, DiscoverySources.Transcript);
            var ocrTask = _p12DiscoveryClient.GetTextAsync(assetId, DiscoverySources.Ocr);
            await Task.WhenAll(categoriesTask, categoryTask, statusTask, tagsTask, refsTask, transcriptTask, ocrTask);

            var categories = await categoriesTask;
            var current = await categoryTask;
            var statuses = await statusTask;
            var tags = await tagsTask;
            var references = await refsTask;
            var transcript = await transcriptTask;
            var ocr = await ocrTask;

            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0) };
            var categoryBox = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 8, 8, 0) };
            foreach (var item in categories)
                categoryBox.Items.Add(new ComboBoxItem { Content = P12CategoryDisplay(item, categories), Tag = item.CategoryId, IsSelected = item.CategoryId == current.Category.CategoryId });
            var saveCategory = P04ActionButton(_arabic ? "حفظ التصنيف" : "Save category");
            var categoryState = new TextBlock { Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            saveCategory.Click += async (_, _) =>
            {
                if (categoryBox.SelectedItem is not ComboBoxItem item || item.Tag is not Guid categoryId) return;
                try { await _p12DiscoveryClient.AssignAssetCategoryAsync(assetId, categoryId); categoryState.Text = _arabic ? "تم تحديث التصنيف." : "Asset category updated."; }
                catch (MamApiException ex) { categoryState.Text = ex.Message; }
            };
            var categoryRow = new WrapPanel(); categoryRow.Children.Add(categoryBox); categoryRow.Children.Add(saveCategory);
            var categoryPanel = new StackPanel(); categoryPanel.Children.Add(categoryRow); categoryPanel.Children.Add(categoryState);
            tabs.Items.Add(new TabItem { Header = _arabic ? "البيانات والتصنيف" : "Metadata & Category", Content = categoryPanel });

            tabs.Items.Add(new TabItem { Header = _arabic ? "التفريغ الزمني" : "Transcript Timeline", Content = BuildP12TextPanel(transcript, statuses, DiscoverySources.Transcript) });
            tabs.Items.Add(new TabItem { Header = "OCR", Content = BuildP12TextPanel(ocr, statuses, DiscoverySources.Ocr) });

            var referencePanel = new StackPanel();
            if (tags.Count == 0) referencePanel.Children.Add(new TextBlock { Text = _arabic ? "لا توجد وسوم مرجعية." : "No reference tags yet.", Foreground = Text() });
            foreach (var tag in tags) referencePanel.Children.Add(new TextBlock { Text = $"• {P12CategoryName(tag.NameEn, tag.NameAr)}{(tag.Confidence is null ? string.Empty : $" · {tag.Confidence:P0}")}", Foreground = Text(), Margin = new Thickness(0, 3, 0, 0) });
            var referenceBox = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 10, 8, 0) };
            referenceBox.Items.Add(new ComboBoxItem { Content = _arabic ? "اختر مرجعًا" : "Select reference subject", Tag = null, IsSelected = true });
            foreach (var reference in references) referenceBox.Items.Add(new ComboBoxItem { Content = P12CategoryName(reference.NameEn, reference.NameAr), Tag = reference.SubjectId });
            var addTag = P04ActionButton(_arabic ? "إضافة وسم" : "Add tag");
            var referenceState = new TextBlock { Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            addTag.Click += async (_, _) =>
            {
                if (referenceBox.SelectedItem is not ComboBoxItem item || item.Tag is not Guid subjectId) return;
                try { await _p12DiscoveryClient.AddAssetReferenceTagAsync(assetId, new AddAssetReferenceTagRequest(subjectId, null, "manual")); referenceState.Text = _arabic ? "تمت إضافة الوسم وفهرسته." : "Reference tag added and indexed."; }
                catch (MamApiException ex) { referenceState.Text = ex.Message; }
            };
            var referenceRow = new WrapPanel(); referenceRow.Children.Add(referenceBox); referenceRow.Children.Add(addTag); referencePanel.Children.Add(referenceRow); referencePanel.Children.Add(referenceState);
            tabs.Items.Add(new TabItem { Header = _arabic ? "الوسوم المرجعية" : "Reference Tags", Content = referencePanel });

            if (string.Equals(mediaType, MediaKinds.Video, StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, MediaKinds.Audio, StringComparison.OrdinalIgnoreCase)) tabs.SelectedIndex = 1;
            else if (string.Equals(mediaType, MediaKinds.Document, StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, MediaKinds.Image, StringComparison.OrdinalIgnoreCase)) tabs.SelectedIndex = 2;
            return Card(_arabic ? "الاكتشاف والفهرسة" : "Discovery & Indexing", tabs);
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return StateCard("Permission denied", _arabic ? "لا توجد صلاحية لبيانات الاكتشاف لهذا الأصل." : "Permission denied for this asset's discovery data.", "#FFF6ED", "#C4320A");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            return StateCard("Degraded", _arabic ? "تعذر تحميل التصنيف أو النصوص المفهرسة." : "Category or indexed text could not be loaded.", "#FFFAEB", "#B54708");
        }
    }

    internal async Task<string?> GetP12ProcessingProgressAsync(Guid assetId, string profileId)
    {
        if (_p12DiscoveryClient is null) return null;
        var kind = string.Equals(profileId, BuiltInProcessingProfiles.OcrText, StringComparison.OrdinalIgnoreCase) ? DiscoverySources.Ocr
            : string.Equals(profileId, BuiltInProcessingProfiles.TranscriptText, StringComparison.OrdinalIgnoreCase) ? DiscoverySources.Transcript : null;
        if (kind is null) return null;
        try
        {
            var status = (await _p12DiscoveryClient.GetExtractionStatusAsync(assetId)).FirstOrDefault(item => string.Equals(item.ExtractionKind, kind, StringComparison.OrdinalIgnoreCase));
            return status is null ? null : $"{status.ProgressPercent}% · {status.State}";
        }
        catch { return null; }
    }

    private FrameworkElement BuildP12TextPanel(AssetTextSnapshot? text, IReadOnlyList<TextExtractionStatusSnapshot> statuses, string kind)
    {
        var panel = new StackPanel();
        var status = statuses.FirstOrDefault(item => string.Equals(item.ExtractionKind, kind, StringComparison.OrdinalIgnoreCase));
        if (status is not null)
            panel.Children.Add(StateCard(status.State, $"{status.ProgressPercent}% · {status.Detail}", status.State == "Failed" ? "#FEF3F2" : status.State == "Succeeded" ? "#ECFDF3" : "#EFF8FF", status.State == "Failed" ? "#B42318" : status.State == "Succeeded" ? "#027A48" : "#175CD3"));
        if (text is null)
        {
            panel.Children.Add(new TextBlock { Text = _arabic ? "لا يوجد نص مفهرس بعد. استخدم إجراء المعالجة لهذا الأصل." : "No indexed text yet. Queue the relevant processing action for this asset.", TextWrapping = TextWrapping.Wrap, Foreground = Text(), Margin = new Thickness(0, 8, 0, 0) });
            return panel;
        }
        if (text.Segments.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = text.Text, TextWrapping = TextWrapping.Wrap, Foreground = Text(), Margin = new Thickness(0, 8, 0, 0) });
            return panel;
        }
        foreach (var segment in text.Segments.Take(500))
        {
            var marker = kind == DiscoverySources.Transcript ? $"{P12Time(segment.StartMs)} → {P12Time(segment.EndMs)}" : segment.PageNumber is not null ? $"{(_arabic ? "صفحة" : "Page")} {segment.PageNumber}" : $"#{segment.SegmentIndex + 1}";
            panel.Children.Add(ListRow(marker, segment.Text, kind, string.Empty));
        }
        return panel;
    }

    private void ShowP12Loading(string route, string title, string detail)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(Lead(title, detail), StateCard("Loading", detail, "#EFF8FF", "#175CD3")));
    }

    private void ShowP12Denied(string route)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(Lead(_arabic ? "الوصول مرفوض" : "Permission denied", string.Empty), StateCard("Permission denied", _arabic ? "لا توجد صلاحية لهذه الصفحة." : "The current identity is not authorized for this page.", "#FFF6ED", "#C4320A")));
    }

    private void ShowP12Error(string route)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(Lead(_arabic ? "الخدمة غير متاحة" : "Discovery unavailable", string.Empty), StateCard("API error", _arabic ? "تعذر الوصول إلى خدمة الاكتشاف المركزية." : "The Central API discovery service could not be reached.", "#FEF3F2", "#B42318")));
    }

    private string P12CategoryDisplay(CategorySnapshot category, IReadOnlyList<CategorySnapshot>? categories = null)
    {
        categories ??= _p12Categories;
        var depth = 0;
        var current = category;
        var guard = 0;
        while (current.ParentCategoryId is Guid parentId && guard++ < 40)
        {
            var parent = categories.FirstOrDefault(item => item.CategoryId == parentId);
            if (parent is null) break;
            depth++;
            current = parent;
        }
        return $"{string.Concat(Enumerable.Repeat("— ", depth))}{P12CategoryName(category.NameEn, category.NameAr)}";
    }

    private string P12CategoryName(string english, string? arabic) => _arabic && !string.IsNullOrWhiteSpace(arabic) ? arabic : english;

    private static string P12Time(long? milliseconds)
    {
        if (milliseconds is null) return "—";
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds.Value));
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}

internal sealed class P12CategoryEditDialog : Window
{
    private readonly TextBox _nameEn;
    private readonly TextBox _nameAr;
    private readonly ComboBox _parent;
    private readonly TextBox _sort;

    public P12CategoryEditDialog(CategorySnapshot category, IReadOnlyList<CategorySnapshot> categories, bool arabic)
    {
        Title = arabic ? "تعديل التصنيف" : "Edit category";
        Width = 520; Height = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White; ResizeMode = ResizeMode.NoResize;
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = arabic ? "الاسم الإنجليزي" : "English name", FontWeight = FontWeights.SemiBold });
        _nameEn = new TextBox { Text = category.NameEn, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8) }; stack.Children.Add(_nameEn);
        stack.Children.Add(new TextBlock { Text = arabic ? "الاسم العربي" : "Arabic name", FontWeight = FontWeights.SemiBold });
        _nameAr = new TextBox { Text = category.NameAr ?? string.Empty, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8), FlowDirection = FlowDirection.RightToLeft }; stack.Children.Add(_nameAr);
        stack.Children.Add(new TextBlock { Text = arabic ? "الأب" : "Parent", FontWeight = FontWeights.SemiBold });
        _parent = new ComboBox { Margin = new Thickness(0, 6, 0, 12) }; _parent.Items.Add(new ComboBoxItem { Content = arabic ? "بدون أب" : "No parent", Tag = null, IsSelected = category.ParentCategoryId is null });
        foreach (var item in categories.Where(item => !item.IsSystem && item.CategoryId != category.CategoryId)) _parent.Items.Add(new ComboBoxItem { Content = arabic && !string.IsNullOrWhiteSpace(item.NameAr) ? item.NameAr : item.NameEn, Tag = item.CategoryId, IsSelected = item.CategoryId == category.ParentCategoryId });
        stack.Children.Add(_parent);
        stack.Children.Add(new TextBlock { Text = arabic ? "الترتيب" : "Sort order", FontWeight = FontWeights.SemiBold });
        _sort = new TextBox { Text = category.SortOrder.ToString(), Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8) }; stack.Children.Add(_sort);
        var save = new Button { Content = arabic ? "حفظ" : "Save", Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(181, 138, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(_nameEn.Text)) return; DialogResult = true; }; stack.Children.Add(save);
        Content = stack;
    }

    public string NameEn => _nameEn.Text.Trim();
    public string? NameAr => string.IsNullOrWhiteSpace(_nameAr.Text) ? null : _nameAr.Text.Trim();
    public Guid? ParentCategoryId => _parent.SelectedItem is ComboBoxItem item && item.Tag is Guid id ? id : null;
    public int SortOrder => int.TryParse(_sort.Text, out var value) ? value : 0;
}
