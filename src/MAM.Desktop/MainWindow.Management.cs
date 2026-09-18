using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Clients;
using MAM.Application.Curation;

namespace MAM.Desktop;

public partial class MainWindow
{
    private Guid? _managementSelectedCollectionId;
    private string _managementCollectionAssetQuery = string.Empty;
    private string _managementTagQuery = string.Empty;

    private async Task LoadManagementCollectionsAsync()
    {
        if (_p05CurationClient is null)
        {
            ShowManagementUnavailable("collections");
            return;
        }

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "إدارة المجموعات" : "Collection Management",
                _arabic ? "إنشاء وتعديل وحذف المجموعات وإدارة الأعضاء بدون حذف أي أصل إعلامي." : "Create, rename and delete collections, and manage membership without deleting media assets."),
            StateCard("Loading", _arabic ? "جاري تحميل المجموعات…" : "Loading collections…", "#EFF8FF", "#175CD3")));

        try
        {
            var collections = await _p05CurationClient.ListCollectionsAsync();
            if (!string.Equals(_currentRoute, "collections", StringComparison.OrdinalIgnoreCase)) return;

            var selected = _managementSelectedCollectionId is Guid id
                ? collections.FirstOrDefault(x => x.CollectionId == id)
                : null;

            CurationSearchResult? members = null;
            CurationSearchResult? candidates = null;
            if (selected is not null)
            {
                members = await _p05CurationClient.SearchAsync(new CurationSearchRequest(
                    CollectionId: selected.CollectionId, Page: 1, PageSize: 100));
                candidates = await _p05CurationClient.SearchAsync(new CurationSearchRequest(
                    Query: string.IsNullOrWhiteSpace(_managementCollectionAssetQuery) ? null : _managementCollectionAssetQuery,
                    Page: 1, PageSize: 100));
                if (!string.Equals(_currentRoute, "collections", StringComparison.OrdinalIgnoreCase)) return;
            }

            var createEn = new TextBox { MinWidth = 220, MaxLength = 200, Padding = new Thickness(8), ToolTip = "Collection name (English)" };
            var createAr = new TextBox { MinWidth = 220, MaxLength = 200, Padding = new Thickness(8), Margin = new Thickness(8, 0, 0, 0), FlowDirection = FlowDirection.RightToLeft, ToolTip = "اسم المجموعة بالعربية" };
            var create = P05ActionButton(_arabic ? "إضافة مجموعة" : "Create collection");
            create.Margin = new Thickness(8, 0, 0, 0);
            var createState = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            create.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(createEn.Text))
                {
                    createState.Text = _arabic ? "الاسم الإنجليزي مطلوب." : "English collection name is required.";
                    return;
                }

                create.IsEnabled = false;
                try
                {
                    await _p05CurationClient.CreateCollectionAsync(new CreateCollectionRequest(
                        createEn.Text.Trim(),
                        string.IsNullOrWhiteSpace(createAr.Text) ? null : createAr.Text.Trim()));
                    await LoadManagementCollectionsAsync();
                }
                catch (MamApiException ex) { createState.Text = ex.Message; }
                finally { create.IsEnabled = true; }
            };
            var createRow = new WrapPanel();
            createRow.Children.Add(createEn);
            createRow.Children.Add(createAr);
            createRow.Children.Add(create);
            var createStack = new StackPanel();
            createStack.Children.Add(createRow);
            createStack.Children.Add(createState);

            var list = new StackPanel();
            foreach (var item in collections)
                list.Children.Add(BuildManagementCollectionRow(item));

            var totalMemberships = collections.Sum(x => x.MemberCount);
            var page = new List<UIElement>
            {
                Lead(_arabic ? "إدارة المجموعات" : "Collection Management",
                    _arabic ? "حذف المجموعة يفك العضويات فقط ولا يحذف الميديا." : "Deleting a collection detaches memberships only; it never deletes media."),
                MetricRow(
                    Metric(collections.Count.ToString("N0"), _arabic ? "المجموعات" : "Collections", "Central API"),
                    Metric(totalMemberships.ToString("N0"), _arabic ? "العضويات" : "Memberships", "Assets remain independent"),
                    Metric(collections.Count(x => x.MemberCount == 0).ToString("N0"), _arabic ? "مجموعات فارغة" : "Empty collections", "Safe to delete")),
                Card(_arabic ? "إنشاء مجموعة" : "Create collection", createStack),
                Card(_arabic ? "المجموعات" : "Collections", list)
            };

            if (selected is not null && members is not null && candidates is not null)
                page.Add(BuildManagementCollectionMembers(selected, members, candidates));

            ContentHost.Content = Scroll(PageStack(page.ToArray()));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowManagementDenied("collections");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowManagementError("collections", ex.Message);
        }
    }

    private FrameworkElement BuildManagementCollectionRow(CollectionSnapshot item)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _arabic && !string.IsNullOrWhiteSpace(item.NameAr) ? item.NameAr : item.NameEn,
            FontWeight = FontWeights.Bold,
            FontSize = 17,
            Foreground = Navy()
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{item.MemberCount:N0} {(_arabic ? "أصل" : "assets")} · v{item.Version}",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Text()
        });

        var actions = new WrapPanel();
        var members = P05SecondaryButton(_arabic ? "الأعضاء" : "Members");
        members.Click += async (_, _) =>
        {
            _managementSelectedCollectionId = item.CollectionId;
            _managementCollectionAssetQuery = string.Empty;
            await LoadManagementCollectionsAsync();
        };

        var edit = P05SecondaryButton(_arabic ? "تعديل" : "Edit");
        edit.Click += async (_, _) =>
        {
            var dialog = new ManagementCollectionDialog(item.NameEn, item.NameAr, _arabic) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                await _p05CurationClient!.UpdateCollectionAsync(item.CollectionId,
                    new UpdateCollectionRequest(item.Version, dialog.NameEn, dialog.NameAr));
                await LoadManagementCollectionsAsync();
            }
            catch (MamApiException ex)
            {
                MessageBox.Show(this, ex.Message, _arabic ? "تعذر التعديل" : "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        var delete = P05SecondaryButton(_arabic ? "حذف" : "Delete");
        delete.Foreground = Brush("#B42318");
        delete.Click += async (_, _) =>
        {
            var message = _arabic
                ? $"سيتم حذف تعريف المجموعة وفك ارتباط {item.MemberCount:N0} أصل منها فقط. لن يتم حذف أي أصل إعلامي.\n\n{item.NameAr ?? item.NameEn}"
                : $"The collection definition will be deleted and {item.MemberCount:N0} memberships detached. No media asset will be deleted.\n\n{item.NameEn}";
            if (MessageBox.Show(this, message, _arabic ? "تأكيد حذف المجموعة" : "Confirm collection deletion",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await _p05CurationClient!.DeleteCollectionAsync(item.CollectionId, item.Version);
                if (_managementSelectedCollectionId == item.CollectionId) _managementSelectedCollectionId = null;
                await LoadManagementCollectionsAsync();
            }
            catch (MamApiException ex)
            {
                MessageBox.Show(this, ex.Message, _arabic ? "تعذر الحذف" : "Delete failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        actions.Children.Add(members);
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        stack.Children.Add(actions);
        return Card(string.Empty, stack);
    }

    private FrameworkElement BuildManagementCollectionMembers(
        CollectionSnapshot collection,
        CurationSearchResult members,
        CurationSearchResult candidates)
    {
        var stack = new StackPanel();

        var header = new WrapPanel();
        var query = new TextBox
        {
            Text = _managementCollectionAssetQuery,
            MinWidth = 280,
            Padding = new Thickness(8),
            ToolTip = _arabic ? "ابحث عن أصل لإضافته" : "Search assets to add"
        };
        var search = P05ActionButton(_arabic ? "بحث" : "Search");
        search.Margin = new Thickness(8, 0, 0, 0);
        search.Click += async (_, _) =>
        {
            _managementCollectionAssetQuery = query.Text.Trim();
            await LoadManagementCollectionsAsync();
        };
        var close = P05SecondaryButton(_arabic ? "إغلاق" : "Close");
        close.Margin = new Thickness(8, 0, 0, 0);
        close.Click += async (_, _) =>
        {
            _managementSelectedCollectionId = null;
            _managementCollectionAssetQuery = string.Empty;
            await LoadManagementCollectionsAsync();
        };
        header.Children.Add(query);
        header.Children.Add(search);
        header.Children.Add(close);
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = $"{(_arabic ? "الأعضاء الحاليون" : "Current members")} ({members.TotalCount:N0})",
            Margin = new Thickness(0, 16, 0, 6),
            FontWeight = FontWeights.Bold,
            Foreground = Navy()
        });
        if (members.Items.Count == 0)
            stack.Children.Add(new TextBlock { Text = _arabic ? "المجموعة فارغة." : "This collection is empty.", Foreground = Text() });

        foreach (var asset in members.Items)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = asset.Title, Width = 320, TextWrapping = TextWrapping.Wrap, Foreground = Text() });
            row.Children.Add(new TextBlock { Text = asset.Id.ToString("D"), Width = 310, Foreground = Text() });
            var remove = P05SecondaryButton(_arabic ? "إزالة" : "Remove");
            remove.Margin = new Thickness(8, 0, 0, 0);
            remove.Click += async (_, _) =>
            {
                try
                {
                    await _p05CurationClient!.RemoveFromCollectionAsync(collection.CollectionId, asset.Id, collection.Version);
                    await LoadManagementCollectionsAsync();
                }
                catch (MamApiException ex) { MessageBox.Show(this, ex.Message); }
            };
            row.Children.Add(remove);
            stack.Children.Add(row);
        }

        var memberIds = members.Items.Select(x => x.Id).ToHashSet();
        var available = candidates.Items.Where(x => !memberIds.Contains(x.Id)).Take(50).ToArray();
        stack.Children.Add(new TextBlock
        {
            Text = _arabic ? "إضافة أصول" : "Add assets",
            Margin = new Thickness(0, 18, 0, 6),
            FontWeight = FontWeights.Bold,
            Foreground = Navy()
        });
        if (available.Length == 0)
            stack.Children.Add(new TextBlock { Text = _arabic ? "لا توجد أصول مطابقة متاحة للإضافة." : "No matching assets are available to add.", Foreground = Text() });

        foreach (var asset in available)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = asset.Title, Width = 320, TextWrapping = TextWrapping.Wrap, Foreground = Text() });
            row.Children.Add(new TextBlock { Text = asset.Id.ToString("D"), Width = 310, Foreground = Text() });
            var add = P05ActionButton(_arabic ? "إضافة" : "Add");
            add.Margin = new Thickness(8, 0, 0, 0);
            add.Click += async (_, _) =>
            {
                try
                {
                    await _p05CurationClient!.AddToCollectionAsync(collection.CollectionId, asset.Id, collection.Version);
                    await LoadManagementCollectionsAsync();
                }
                catch (MamApiException ex) { MessageBox.Show(this, ex.Message); }
            };
            row.Children.Add(add);
            stack.Children.Add(row);
        }

        return Card($"{(_arabic ? "إدارة أعضاء" : "Members of")} {(_arabic && !string.IsNullOrWhiteSpace(collection.NameAr) ? collection.NameAr : collection.NameEn)}", stack);
    }

    private async Task LoadManagementTagsAsync()
    {
        if (_p05CurationClient is null)
        {
            ShowManagementUnavailable("tags");
            return;
        }

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "إدارة الوسوم" : "Tag Management",
                _arabic ? "قاموس مركزي مع عدادات استخدام وإعادة تسمية تنتشر على الأصول." : "Authoritative dictionary with usage counts and propagated rename."),
            StateCard("Loading", _arabic ? "جاري تحميل الوسوم…" : "Loading tags…", "#EFF8FF", "#175CD3")));

        try
        {
            var tags = await _p05CurationClient.ListTagsAsync(string.IsNullOrWhiteSpace(_managementTagQuery) ? null : _managementTagQuery);
            if (!string.Equals(_currentRoute, "tags", StringComparison.OrdinalIgnoreCase)) return;

            var createName = new TextBox { MinWidth = 260, MaxLength = 120, Padding = new Thickness(8), ToolTip = _arabic ? "اسم الوسم" : "Tag name" };
            var create = P05ActionButton(_arabic ? "إضافة وسم" : "Create tag");
            create.Margin = new Thickness(8, 0, 0, 0);
            var feedback = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
            create.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(createName.Text))
                {
                    feedback.Text = _arabic ? "اسم الوسم مطلوب." : "Tag name is required.";
                    return;
                }
                try
                {
                    await _p05CurationClient.CreateTagAsync(new CreateTagRequest(createName.Text.Trim()));
                    await LoadManagementTagsAsync();
                }
                catch (MamApiException ex) { feedback.Text = ex.Message; }
            };
            var createRow = new WrapPanel();
            createRow.Children.Add(createName);
            createRow.Children.Add(create);
            var createStack = new StackPanel();
            createStack.Children.Add(createRow);
            createStack.Children.Add(feedback);

            var searchBox = new TextBox { Text = _managementTagQuery, MinWidth = 280, Padding = new Thickness(8), ToolTip = _arabic ? "بحث في الوسوم" : "Search tags" };
            var search = P05ActionButton(_arabic ? "بحث" : "Search");
            search.Margin = new Thickness(8, 0, 0, 0);
            search.Click += async (_, _) => { _managementTagQuery = searchBox.Text.Trim(); await LoadManagementTagsAsync(); };
            var clear = P05SecondaryButton(_arabic ? "مسح" : "Clear");
            clear.Margin = new Thickness(8, 0, 0, 0);
            clear.Click += async (_, _) => { _managementTagQuery = string.Empty; await LoadManagementTagsAsync(); };
            var searchRow = new WrapPanel();
            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(search);
            searchRow.Children.Add(clear);

            var list = new StackPanel();
            foreach (var tag in tags)
                list.Children.Add(BuildManagementTagRow(tag));

            var listStack = new StackPanel();
            listStack.Children.Add(searchRow);
            listStack.Children.Add(list);

            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إدارة الوسوم" : "Tag Management",
                    _arabic ? "إعادة تسمية الوسم تطبق الاسم الجديد على كل الأصول التي تستخدمه." : "Renaming a tag propagates the canonical name to every asset that uses it."),
                MetricRow(
                    Metric(tags.Count.ToString("N0"), _arabic ? "الوسوم" : "Tags", "Authoritative dictionary"),
                    Metric(tags.Sum(x => x.AssetCount).ToString("N0"), _arabic ? "مرات الاستخدام" : "Assignments", "Across assets"),
                    Metric(tags.Count(x => x.AssetCount == 0).ToString("N0"), _arabic ? "غير مستخدم" : "Unused", "Safe delete")),
                Card(_arabic ? "إضافة وسم" : "Create tag", createStack),
                Card(_arabic ? "قاموس الوسوم" : "Tag dictionary", listStack)));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowManagementDenied("tags");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowManagementError("tags", ex.Message);
        }
    }

    private FrameworkElement BuildManagementTagRow(TagSnapshot tag)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = tag.Name, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Navy() });
        stack.Children.Add(new TextBlock
        {
            Text = $"{tag.AssetCount:N0} {(_arabic ? "أصل" : "assets")} · v{tag.Version}",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Text()
        });

        var actions = new WrapPanel();
        var rename = P05SecondaryButton(_arabic ? "إعادة تسمية" : "Rename");
        rename.Click += async (_, _) =>
        {
            var dialog = new ManagementTagDialog(tag.Name, _arabic) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                await _p05CurationClient!.UpdateTagAsync(tag.TagId, new UpdateTagRequest(tag.Version, dialog.TagName));
                await LoadManagementTagsAsync();
            }
            catch (MamApiException ex) { MessageBox.Show(this, ex.Message); }
        };

        var view = P05SecondaryButton(_arabic ? "عرض الأصول" : "View assets");
        view.Click += async (_, _) => await ShowManagementTagAssetsAsync(tag.Name);

        var delete = P05SecondaryButton(_arabic ? "حذف" : "Delete");
        delete.Foreground = Brush("#B42318");
        delete.Click += async (_, _) =>
        {
            var inUse = tag.AssetCount > 0;
            var message = inUse
                ? (_arabic
                    ? $"الوسم مستخدم على {tag.AssetCount:N0} أصل. سيتم إزالته من هذه الأصول ثم حذف تعريفه."
                    : $"This tag is used by {tag.AssetCount:N0} assets. It will be removed from those assets before deletion.")
                : (_arabic ? "الوسم غير مستخدم ويمكن حذفه بأمان." : "This tag is unused and can be deleted safely.");
            if (MessageBox.Show(this, $"{message}\n\n{tag.Name}", _arabic ? "تأكيد حذف الوسم" : "Confirm tag deletion",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await _p05CurationClient!.DeleteTagAsync(tag.TagId, tag.Version, inUse);
                await LoadManagementTagsAsync();
            }
            catch (MamApiException ex) { MessageBox.Show(this, ex.Message); }
        };

        actions.Children.Add(rename);
        actions.Children.Add(view);
        actions.Children.Add(delete);
        stack.Children.Add(actions);
        return Card(string.Empty, stack);
    }

    private async Task ShowManagementTagAssetsAsync(string tag)
    {
        if (_p05CurationClient is null) return;
        try
        {
            var result = await _p05CurationClient.SearchAsync(new CurationSearchRequest(Tag: tag, Page: 1, PageSize: 100));
            if (!string.Equals(_currentRoute, "tags", StringComparison.OrdinalIgnoreCase)) return;
            var rows = new StackPanel();
            foreach (var asset in result.Items)
                rows.Children.Add(ListRow(asset.Id.ToString("D"), asset.Title, asset.Category ?? "—", asset.Lifecycle));
            if (result.Items.Count == 0)
                rows.Children.Add(new TextBlock { Text = _arabic ? "لا توجد أصول تستخدم هذا الوسم." : "No assets use this tag.", Foreground = Text() });

            var back = P05SecondaryButton(_arabic ? "رجوع للوسوم" : "Back to tags");
            back.Click += async (_, _) => await LoadManagementTagsAsync();
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? $"الأصول الموسومة: {tag}" : $"Assets tagged: {tag}", $"{result.TotalCount:N0}"),
                Card(_arabic ? "الأصول" : "Assets", rows),
                back));
        }
        catch (MamApiException ex) { ShowManagementError("tags", ex.Message); }
    }

    private void ShowManagementUnavailable(string route)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "الخدمة غير مهيأة" : "Service unavailable", string.Empty),
            StateCard("Degraded", _arabic ? "الخدمة المركزية غير مهيأة لهذا العميل." : "The Central API is not configured for this client.", "#FFFAEB", "#B54708")));
    }

    private void ShowManagementDenied(string route)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "الوصول مرفوض" : "Permission denied", string.Empty),
            StateCard("Permission denied", _arabic ? "لا توجد صلاحية لإدارة هذه البيانات." : "The current identity is not authorized to manage this data.", "#FFF6ED", "#C4320A")));
    }

    private void ShowManagementError(string route, string detail)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "تعذر تحميل الصفحة" : "Management unavailable", string.Empty),
            StateCard("API error", detail, "#FEF3F2", "#B42318")));
    }
}

internal sealed class ManagementCollectionDialog : Window
{
    private readonly TextBox _nameEn;
    private readonly TextBox _nameAr;

    public ManagementCollectionDialog(string nameEn, string? nameAr, bool arabic)
    {
        Title = arabic ? "تعديل المجموعة" : "Edit collection";
        Width = 500;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = arabic ? "الاسم الإنجليزي" : "English name", FontWeight = FontWeights.SemiBold });
        _nameEn = new TextBox { Text = nameEn, MaxLength = 200, Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 14) };
        stack.Children.Add(_nameEn);
        stack.Children.Add(new TextBlock { Text = arabic ? "الاسم العربي" : "Arabic name", FontWeight = FontWeights.SemiBold });
        _nameAr = new TextBox { Text = nameAr ?? string.Empty, MaxLength = 200, Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 16), FlowDirection = FlowDirection.RightToLeft };
        stack.Children.Add(_nameAr);
        var save = new Button { Content = arabic ? "حفظ" : "Save", Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(181, 138, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_nameEn.Text)) DialogResult = true; };
        stack.Children.Add(save);
        Content = stack;
    }

    public string NameEn => _nameEn.Text.Trim();
    public string? NameAr => string.IsNullOrWhiteSpace(_nameAr.Text) ? null : _nameAr.Text.Trim();
}

internal sealed class ManagementTagDialog : Window
{
    private readonly TextBox _name;

    public ManagementTagDialog(string name, bool arabic)
    {
        Title = arabic ? "إعادة تسمية الوسم" : "Rename tag";
        Width = 460;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = arabic ? "الاسم الجديد" : "New name", FontWeight = FontWeights.SemiBold });
        _name = new TextBox { Text = name, MaxLength = 120, Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 16) };
        stack.Children.Add(_name);
        var save = new Button { Content = arabic ? "حفظ" : "Save", Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(181, 138, 42)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_name.Text)) DialogResult = true; };
        stack.Children.Add(save);
        Content = stack;
    }

    public string TagName => _name.Text.Trim();
}
