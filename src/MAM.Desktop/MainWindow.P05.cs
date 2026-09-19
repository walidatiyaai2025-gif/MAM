using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Clients;
using MAM.Application.Curation;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamCurationApiClient? _p05CurationClient;
    private HttpClient? _p05HttpClient;
    private string _p05Query = string.Empty;
    private string? _p05Lifecycle;
    private string? _p05Category;
    private bool _p05GridView;

    private void InitializeP05CurationIntegration()
    {
        if (_p05CurationClient is not null) return;
        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri)) return;
        _p05HttpClient = new HttpClient { BaseAddress = EnsureTrailingSlash(apiUri), Timeout = TimeSpan.FromSeconds(30) };
        _p05CurationClient = new MamCurationApiClient(_p05HttpClient, "WindowsDesktop", Environment.GetEnvironmentVariable("MAM_DEV_USER"));
        if (string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase)) _ = LoadP05LibraryAsync();
    }

    private async Task LoadP05LibraryAsync()
    {
        if (_p05CurationClient is null) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "بحث وتهيئة بيانات عبر الخدمة المركزية." : "Authoritative search and metadata curation through the Central API."),
            StateCard("Loading", _arabic ? "جاري تحميل البحث والمرشحات…" : "Loading search results and facets…", "#EFF8FF", "#175CD3")));
        try
        {
            var result = await _p05CurationClient.SearchAsync(new CurationSearchRequest(_p05Query, _p05Lifecycle, _p05Category, PageSize: 50));
            var collections = await _p05CurationClient.ListCollectionsAsync();
            var policy = await _p05CurationClient.GetPolicyAsync();
            if (!string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase)) return;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? $"{result.TotalCount:N0} أصل مطابق · نتائج مركزية" : $"{result.TotalCount:N0} matching assets · authoritative results"),
                BuildP05SearchToolbar(result),
                BuildP05CollectionCard(collections, policy),
                BuildP05Results(result, collections)));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP05State("Permission denied", _arabic ? "لا توجد صلاحية للوصول إلى البحث أو التهيئة." : "The current identity is not authorized for archive curation.", "#FFF6ED", "#C4320A");
        }
        catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            ShowP05State("Degraded", _arabic ? "خدمة البحث والتهيئة غير جاهزة." : "The authoritative search/curation service is degraded.", "#FFFAEB", "#B54708");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowP05State("API error", _arabic ? "تعذر تحميل البحث. يمكن إعادة المحاولة." : "Search could not be loaded. Retry is available.", "#FEF3F2", "#B42318");
        }
    }

    private FrameworkElement BuildP05SearchToolbar(CurationSearchResult result)
    {
        var query = new TextBox
        {
            Text = _p05Query,
            MinWidth = 250,
            MaxLength = 300,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 6, 8, 0),
            ToolTip = _arabic ? "بحث عربي أو إنجليزي" : "Arabic or English search"
        };
        var lifecycle = new ComboBox { MinWidth = 145, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
        lifecycle.Items.Add(_arabic ? "كل الحالات" : "All lifecycle states");
        foreach (var facet in result.Facets.Lifecycles) lifecycle.Items.Add(facet.Value);
        lifecycle.SelectedItem = _p05Lifecycle ?? lifecycle.Items[0];

        var category = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
        category.Items.Add(_arabic ? "كل التصنيفات" : "All categories");
        foreach (var facet in result.Facets.Categories) category.Items.Add(facet.Value);
        category.SelectedItem = _p05Category ?? category.Items[0];

        var search = P05ActionButton(_arabic ? "بحث" : "Search");
        search.Click += async (_, _) =>
        {
            _p05Query = query.Text.Trim();
            _p05Lifecycle = lifecycle.SelectedIndex <= 0 ? null : lifecycle.SelectedItem?.ToString();
            _p05Category = category.SelectedIndex <= 0 ? null : category.SelectedItem?.ToString();
            await LoadP05LibraryAsync();
        };
        var view = P05ActionButton(_p05GridView ? (_arabic ? "عرض قائمة" : "List view") : (_arabic ? "عرض شبكي" : "Grid view"));
        view.Click += async (_, _) => { _p05GridView = !_p05GridView; await LoadP05LibraryAsync(); };
        var reset = P05SecondaryButton(_arabic ? "مسح المرشحات" : "Reset filters");
        reset.Click += async (_, _) => { _p05Query = string.Empty; _p05Lifecycle = null; _p05Category = null; await LoadP05LibraryAsync(); };

        var row = new WrapPanel();
        row.Children.Add(query);
        row.Children.Add(lifecycle);
        row.Children.Add(category);
        row.Children.Add(search);
        row.Children.Add(view);
        row.Children.Add(reset);
        return Card(_arabic ? "البحث والمرشحات" : "Search & facets", row);
    }

    private FrameworkElement BuildP05CollectionCard(IReadOnlyList<CollectionSnapshot> collections, CurationPolicy policy)
    {
        var nameEn = new TextBox { MinWidth = 210, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(10, 8, 10, 8), ToolTip = "Collection name (English)" };
        var nameAr = new TextBox { MinWidth = 210, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(10, 8, 10, 8), ToolTip = "اسم المجموعة بالعربية" };
        var create = P05ActionButton(_arabic ? "إنشاء مجموعة" : "Create collection");
        var feedback = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
        create.Click += async (_, _) =>
        {
            if (_p05CurationClient is null) return;
            try
            {
                await _p05CurationClient.CreateCollectionAsync(new CreateCollectionRequest(nameEn.Text.Trim(), nameAr.Text.Trim()));
                await LoadP05LibraryAsync();
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                feedback.Text = _arabic ? "لا توجد صلاحية لإنشاء المجموعات." : "Permission denied for collection creation.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                feedback.Text = _arabic ? "تعذر إنشاء المجموعة." : "Collection creation failed.";
            }
        };
        var fields = new WrapPanel();
        fields.Children.Add(nameEn);
        fields.Children.Add(nameAr);
        fields.Children.Add(create);
        var stack = new StackPanel();
        stack.Children.Add(fields);
        stack.Children.Add(new TextBlock
        {
            Text = collections.Count == 0
                ? (_arabic ? "لا توجد مجموعات بعد." : "No collections yet.")
                : string.Join(" · ", collections.Take(8).Select(c => $"{(_arabic && !string.IsNullOrWhiteSpace(c.NameAr) ? c.NameAr : c.NameEn)} ({c.MemberCount})")),
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text()
        });
        stack.Children.Add(new TextBlock
        {
            Text = policy.SavedFiltersSupported
                ? (_arabic ? "المرشحات المحفوظة مفعلة." : "Saved filters are enabled.")
                : (_arabic ? "المرشحات المحفوظة غير مفعلة حتى اعتماد السياسة رسميًا." : "Saved filters remain disabled until product policy is explicitly approved."),
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text()
        });
        stack.Children.Add(feedback);
        return Card(_arabic ? "المجموعات والسياسة" : "Collections & policy", stack);
    }

    private FrameworkElement BuildP05Results(CurationSearchResult result, IReadOnlyList<CollectionSnapshot> collections)
    {
        if (result.Items.Count == 0)
            return StateCard("Empty", _arabic ? "لا توجد نتائج تطابق المرشحات الحالية." : "No assets match the current search and filters.", "#F9FAFB", "#475467");

        if (_p05GridView)
        {
            var grid = new WrapPanel();
            foreach (var asset in result.Items) grid.Children.Add(BuildP05AssetCard(asset, collections, 360));
            return grid;
        }

        var list = new StackPanel();
        foreach (var asset in result.Items) list.Children.Add(BuildP05AssetCard(asset, collections, null));
        return list;
    }

    private FrameworkElement BuildP05AssetCard(CurationAssetItem asset, IReadOnlyList<CollectionSnapshot> collections, double? width)
    {
        var title = _arabic && !string.IsNullOrWhiteSpace(asset.TitleAr) ? asset.TitleAr : asset.Title;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Navy(), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock
        {
            Text = $"{asset.Id:D}\nv{asset.Version} · {asset.Lifecycle} · {asset.Category ?? "—"}\n{string.Join(" · ", asset.Tags.Take(8))}",
            Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text()
        });
        var actions = new WrapPanel();
        var edit = P05ActionButton(_arabic ? "تعديل البيانات" : "Edit metadata");
        edit.Click += async (_, _) => await ShowP05MetadataEditorAsync(asset.Id);
        actions.Children.Add(edit);
        if (collections.Count > 0)
        {
            var collection = new ComboBox { MinWidth = 190, Margin = new Thickness(8, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
            collection.Items.Add(new ComboBoxItem { Content = _arabic ? "اختر مجموعة" : "Select collection", Tag = null, IsSelected = true });
            foreach (var item in collections)
                collection.Items.Add(new ComboBoxItem { Content = _arabic && !string.IsNullOrWhiteSpace(item.NameAr) ? item.NameAr : item.NameEn, Tag = item });
            var add = P05SecondaryButton(_arabic ? "إضافة للمجموعة" : "Add to collection");
            add.Click += async (_, _) =>
            {
                if (_p05CurationClient is null || collection.SelectedItem is not ComboBoxItem selected || selected.Tag is not CollectionSnapshot target) return;
                try { await _p05CurationClient.AddToCollectionAsync(target.CollectionId, asset.Id, target.Version); }
                catch { }
                await LoadP05LibraryAsync();
            };
            actions.Children.Add(collection);
            actions.Children.Add(add);
        }
        stack.Children.Add(actions);
        var card = Card(string.Empty, stack);
        if (width is double fixedWidth) card.Width = fixedWidth;
        return card;
    }

    private async Task ShowP05MetadataEditorAsync(Guid assetId)
    {
        if (_p05CurationClient is null) return;
        try
        {
            var metadata = await _p05CurationClient.GetMetadataAsync(assetId);
            if (metadata is null) { ShowP05State("Empty", _arabic ? "الأصل غير موجود." : "Asset was not found.", "#F9FAFB", "#475467"); return; }
            InitializeP12DiscoveryIntegration();
            var categories = _p12DiscoveryClient is null ? _p12Categories : await _p12DiscoveryClient.ListCategoriesAsync();
            var tagDictionary = await _p05CurationClient.ListTagsAsync();
            var titleEn = P05TextBox(metadata.TitleEn, 300);
            var titleAr = P05TextBox(metadata.TitleAr ?? string.Empty, 300);
            var category = new ComboBox { MinWidth = 260, Padding = new Thickness(8, 6, 8, 6) };
            category.Items.Add(new ComboBoxItem { Content = _arabic ? "بدون تصنيف" : "Uncategorized", Tag = string.Empty, IsSelected = string.IsNullOrWhiteSpace(metadata.Category) });
            var categoryMatched = string.IsNullOrWhiteSpace(metadata.Category);
            foreach (var item in categories)
            {
                var value = string.IsNullOrWhiteSpace(item.NameEn) ? item.NameAr : item.NameEn;
                if (string.IsNullOrWhiteSpace(value)) continue;
                var selected = string.Equals(value, metadata.Category, StringComparison.OrdinalIgnoreCase);
                categoryMatched |= selected;
                category.Items.Add(new ComboBoxItem
                {
                    Content = _arabic && !string.IsNullOrWhiteSpace(item.NameAr) ? item.NameAr : value,
                    Tag = value,
                    IsSelected = selected
                });
            }
            if (!categoryMatched && !string.IsNullOrWhiteSpace(metadata.Category))
                category.Items.Add(new ComboBoxItem { Content = metadata.Category, Tag = metadata.Category, IsSelected = true });
            var tags = new ListBox
            {
                SelectionMode = SelectionMode.Multiple,
                MinHeight = 130,
                MaxHeight = 240,
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6)
            };
            var tagNames = tagDictionary.Select(x => x.Name)
                .Concat(metadata.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            foreach (var tagName in tagNames)
            {
                var item = new ListBoxItem { Content = tagName };
                if (metadata.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase)) item.IsSelected = true;
                tags.Items.Add(item);
            }
            var tagAddName = new TextBox { MinWidth = 220, MaxLength = 120, Padding = new Thickness(8), Margin = new Thickness(0, 8, 8, 0), ToolTip = _arabic ? "اسم وسم جديد" : "New tag name" };
            var tagAdd = P05SecondaryButton(_arabic ? "إضافة وسم جديد" : "Create new tag");
            var tagPanel = new StackPanel();
            tagPanel.Children.Add(tags);
            var tagActions = new WrapPanel();
            tagActions.Children.Add(tagAddName);
            tagActions.Children.Add(tagAdd);
            tagPanel.Children.Add(tagActions);
            tagAdd.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(tagAddName.Text)) return;
                try
                {
                    await _p05CurationClient.CreateTagAsync(new CreateTagRequest(tagAddName.Text.Trim()));
                    await ShowP05MetadataEditorAsync(assetId);
                }
                catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    await ShowP05MetadataEditorAsync(assetId);
                }
                catch (MamApiException ex)
                {
                    MessageBox.Show(this, ex.Message, _arabic ? "تعذر إضافة الوسم" : "Tag creation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            var notes = P05TextBox(metadata.PreservationNotes ?? string.Empty, 2000, true);
            var feedback = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            var save = P05ActionButton(_arabic ? "حفظ البيانات" : "Save metadata");
            save.Click += async (_, _) =>
            {
                try
                {
                    var updated = await _p05CurationClient.UpdateMetadataAsync(assetId, new AssetMetadataUpdateRequest(
                        metadata.Version,
                        "core-media-v1",
                        titleEn.Text.Trim(),
                        titleAr.Text.Trim(),
                        metadata.EventDate,
                        (category.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
                        tags.SelectedItems.OfType<ListBoxItem>().Select(item => item.Content?.ToString() ?? string.Empty).Where(value => value.Length > 0).ToArray(),
                        notes.Text.Trim()));
                    feedback.Text = _arabic ? $"تم الحفظ · الإصدار {updated.Version}" : $"Saved · version {updated.Version}";
                    await ShowP05MetadataEditorAsync(assetId);
                }
                catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    feedback.Text = _arabic ? "تعارض إصدار: حدّث البيانات ثم أعد المحاولة." : "Version conflict: refresh the asset before retrying.";
                }
                catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    feedback.Text = _arabic ? "لا توجد صلاحية للتعديل." : "Permission denied for metadata editing.";
                }
                catch { feedback.Text = _arabic ? "تعذر حفظ البيانات." : "Metadata save failed."; }
            };
            var lifecycle = P05SecondaryButton(metadata.Lifecycle == "Archived" ? (_arabic ? "استعادة" : "Restore") : (_arabic ? "أرشفة" : "Archive"));
            lifecycle.Click += async (_, _) =>
            {
                try
                {
                    if (metadata.Lifecycle == "Archived") await _p05CurationClient.RestoreAsync(assetId, metadata.Version);
                    else await _p05CurationClient.ArchiveAsync(assetId, metadata.Version);
                    await LoadP05LibraryAsync();
                }
                catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { feedback.Text = _arabic ? "تعارض إصدار." : "Version conflict."; }
                catch { feedback.Text = _arabic ? "تعذر تغيير حالة الأصل." : "Lifecycle update failed."; }
            };
            var back = P05SecondaryButton(_arabic ? "رجوع للمكتبة" : "Back to library");
            back.Click += async (_, _) => await LoadP05LibraryAsync();
            var actions = new WrapPanel(); actions.Children.Add(save); actions.Children.Add(lifecycle); actions.Children.Add(back);
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "تهيئة البيانات الوصفية" : "Metadata Curation", $"{assetId:D} · v{metadata.Version} · {metadata.Lifecycle}"),
                Card(_arabic ? "العنوان الإنجليزي" : "English title", titleEn),
                Card(_arabic ? "العنوان العربي" : "Arabic title", titleAr),
                TwoColumn(Card(_arabic ? "التصنيف" : "Category", category), Card(_arabic ? "الوسوم" : "Tags", tagPanel)),
                Card(_arabic ? "ملاحظات الحفظ" : "Preservation notes", notes),
                Card(_arabic ? "الإجراءات" : "Actions", actions),
                feedback));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP05State("Permission denied", _arabic ? "لا توجد صلاحية لعرض البيانات الوصفية." : "Permission denied for metadata.", "#FFF6ED", "#C4320A");
        }
        catch { ShowP05State("API error", _arabic ? "تعذر تحميل البيانات الوصفية." : "Metadata could not be loaded.", "#FEF3F2", "#B42318"); }
    }

    private void ShowP05State(string title, string detail, string background, string foreground)
    {
        if (!string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "حالة البحث والتهيئة المركزية." : "Central search and curation state."),
            StateCard(title, detail, background, foreground)));
    }

    private static TextBox P05TextBox(string text, int maxLength, bool multiline = false) => new()
    {
        Text = text,
        MaxLength = maxLength,
        MinHeight = multiline ? 100 : 38,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Padding = new Thickness(10, 8, 10, 8)
    };

    private static Button P05ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 8, 8, 0),
        Padding = new Thickness(14, 9, 14, 9),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = Gold(),
        Foreground = System.Windows.Media.Brushes.White,
        BorderThickness = new Thickness(0)
    };

    private static Button P05SecondaryButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 8, 8, 0),
        Padding = new Thickness(14, 9, 14, 9),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = System.Windows.Media.Brushes.White,
        Foreground = Navy(),
        BorderBrush = Gold(),
        BorderThickness = new Thickness(1)
    };
}
