using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MAM.Application.Administration;
using MAM.Application.Curation;
using MAM.Application.Clients;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly bool P12FunctionActionsLoadedHandlerRegistered = RegisterP12FunctionActionsLoadedHandler();
    private bool _p12FunctionActionsWired;

    private static bool RegisterP12FunctionActionsLoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P12FunctionActionsWindowLoaded));
        return true;
    }

    private static void P12FunctionActionsWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP12FunctionActions();
    }

    private void WireP12FunctionActions()
    {
        if (_p12FunctionActionsWired) return;
        _p12FunctionActionsWired = true;
        _titles["curation-actions"] = ("Curation Actions", "إجراءات التهيئة");
        _titles["admin-actions"] = ("Management Actions", "إجراءات الإدارة");

        var curation = NewP12NavButton("curation-actions");
        var library = NavPanel.Children.OfType<Button>().FirstOrDefault(x => string.Equals(x.Tag as string, "library", StringComparison.OrdinalIgnoreCase));
        var libraryIndex = library is null ? 1 : NavPanel.Children.IndexOf(library) + 1;
        NavPanel.Children.Insert(Math.Min(libraryIndex, NavPanel.Children.Count), curation);

        var adminActions = NewP12NavButton("admin-actions");
        var settings = NavPanel.Children.OfType<Button>().FirstOrDefault(x => string.Equals(x.Tag as string, "settings", StringComparison.OrdinalIgnoreCase));
        var settingsIndex = settings is null ? NavPanel.Children.Count : NavPanel.Children.IndexOf(settings);
        NavPanel.Children.Insert(settingsIndex, adminActions);

        curation.Click += async (_, _) => await NavigateP12FunctionRouteAsync("curation-actions");
        adminActions.Click += async (_, _) => await NavigateP12FunctionRouteAsync("admin-actions");
        LanguageButton.Click += async (_, _) =>
        {
            if (_currentRoute == "curation-actions") await ShowP12CurationActionsAsync();
            if (_currentRoute == "admin-actions") await ShowP12AdministrationActionsAsync();
        };
    }

    private Button NewP12NavButton(string route) => new()
    {
        Tag = route,
        Content = _arabic ? _titles[route].Ar : _titles[route].En,
        Style = (Style)FindResource("NavButton")
    };

    private async Task NavigateP12FunctionRouteAsync(string route)
    {
        _currentRoute = route;
        PageTitle.Text = _arabic ? _titles[route].Ar : _titles[route].En;
        if (route == "curation-actions") await ShowP12CurationActionsAsync();
        else await ShowP12AdministrationActionsAsync();
    }

    private async Task ShowP12CurationActionsAsync()
    {
        if (_currentRoute != "curation-actions") return;
        if (_p05CurationClient is null)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إجراءات التهيئة" : "Curation Actions", _arabic ? "واجهة API المركزية غير مهيأة." : "Central API is not configured."),
                StateCard("Degraded", _arabic ? "لا يمكن تنفيذ عمليات التهيئة بدون الخدمة المركزية." : "Curation actions require the Central API.", "#FFFAEB", "#B54708")));
            return;
        }

        try
        {
            var collections = await _p05CurationClient.ListCollectionsAsync();
            var policy = await _p05CurationClient.GetPolicyAsync();
            if (_currentRoute != "curation-actions") return;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إجراءات التهيئة" : "Curation Actions",
                    _arabic ? "وظائف التحرير الجماعي وعضوية المجموعات لها أزرار صريحة ونتيجة ظاهرة." : "Bulk metadata and collection membership operations have explicit controls and visible outcomes."),
                BuildP12BulkMetadataCard(policy),
                BuildP12CollectionMembershipCard(collections)));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP12ActionFailure("curation-actions", _arabic ? "لا توجد صلاحية لإجراءات التهيئة." : "Permission denied for curation actions.");
        }
        catch
        {
            ShowP12ActionFailure("curation-actions", _arabic ? "تعذر تحميل إجراءات التهيئة." : "Curation actions could not be loaded.");
        }
    }

    private FrameworkElement BuildP12BulkMetadataCard(CurationPolicy policy)
    {
        var ids = P12TextBox(string.Empty, 1500, true);
        ids.MinHeight = 90;
        ids.ToolTip = _arabic ? "معرّف أصل واحد في كل سطر" : "One asset GUID per line";
        var category = P12TextBox(string.Empty, 120);
        category.ToolTip = _arabic ? "تصنيف جديد؛ اتركه فارغًا للاحتفاظ بالقيمة الحالية" : "New category; leave blank to preserve current value";
        var tags = P12TextBox(string.Empty, 1000);
        tags.ToolTip = _arabic ? "وسوم مفصولة بفواصل؛ اتركها فارغة للاحتفاظ بالقيمة الحالية" : "Comma-separated tags; leave blank to preserve current value";
        var notes = P12TextBox(string.Empty, 2000, true);
        notes.MinHeight = 70;
        notes.ToolTip = _arabic ? "ملاحظات الحفظ؛ اتركها فارغة للاحتفاظ بالقيمة الحالية" : "Preservation notes; leave blank to preserve current value";
        var state = P12StateText();
        var apply = P12ActionButton(_arabic ? "تطبيق تعديل جماعي" : "Apply bulk metadata");
        apply.Click += async (_, _) =>
        {
            if (_p05CurationClient is null) return;
            var assetIds = ids.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(policy.MaxBulkItems)
                .ToArray();
            if (assetIds.Length == 0)
            {
                state.Text = _arabic ? "أدخل معرّف أصل صحيحًا واحدًا على الأقل." : "Enter at least one valid asset GUID.";
                return;
            }
            apply.IsEnabled = false;
            state.Text = _arabic ? "جاري تجهيز الحالة الحالية ثم تنفيذ التعديل الجماعي…" : "Loading current versions and applying the bulk update…";
            try
            {
                var items = new List<BulkMetadataItem>();
                foreach (var assetId in assetIds)
                {
                    var current = await _p05CurationClient.GetMetadataAsync(assetId);
                    if (current is null) continue;
                    items.Add(new BulkMetadataItem(
                        assetId,
                        current.Version,
                        current.SchemaKey,
                        current.TitleEn,
                        current.TitleAr,
                        current.EventDate,
                        string.IsNullOrWhiteSpace(category.Text) ? current.Category : category.Text.Trim(),
                        string.IsNullOrWhiteSpace(tags.Text) ? current.Tags : tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        string.IsNullOrWhiteSpace(notes.Text) ? current.PreservationNotes : notes.Text.Trim()));
                }
                if (items.Count == 0)
                {
                    state.Text = _arabic ? "لم يتم العثور على أصول صالحة للتعديل." : "No matching assets were available for update.";
                    return;
                }
                var result = await _p05CurationClient.BulkUpdateMetadataAsync(new BulkMetadataRequest(items));
                state.Text = _arabic
                    ? $"تم الطلب: {result.Requested} · نجح: {result.Succeeded} · فشل: {result.Failed}"
                    : $"Requested {result.Requested} · succeeded {result.Succeeded} · failed {result.Failed}";
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { state.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { state.Text = _arabic ? "فشل التعديل الجماعي." : "Bulk metadata update failed."; }
            finally { apply.IsEnabled = true; }
        };

        var stack = new StackPanel();
        stack.Children.Add(P12Label(_arabic ? $"معرّفات الأصول · الحد الأقصى {policy.MaxBulkItems}" : $"Asset IDs · maximum {policy.MaxBulkItems}"));
        stack.Children.Add(ids);
        stack.Children.Add(P12Label(_arabic ? "التصنيف" : "Category"));
        stack.Children.Add(category);
        stack.Children.Add(P12Label(_arabic ? "الوسوم" : "Tags"));
        stack.Children.Add(tags);
        stack.Children.Add(P12Label(_arabic ? "ملاحظات الحفظ" : "Preservation notes"));
        stack.Children.Add(notes);
        stack.Children.Add(apply);
        stack.Children.Add(state);
        return Card(_arabic ? "تعديل البيانات الوصفية جماعيًا" : "Bulk metadata", stack);
    }

    private FrameworkElement BuildP12CollectionMembershipCard(IReadOnlyList<CollectionSnapshot> collections)
    {
        var combo = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var collection in collections)
            combo.Items.Add(new ComboBoxItem { Content = _arabic && !string.IsNullOrWhiteSpace(collection.NameAr) ? collection.NameAr : collection.NameEn, Tag = collection });
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        var assetId = P12TextBox(string.Empty, 80);
        assetId.MinWidth = 310;
        assetId.ToolTip = _arabic ? "معرّف الأصل GUID" : "Asset GUID";
        var state = P12StateText();
        var add = P12ActionButton(_arabic ? "إضافة للمجموعة" : "Add to collection");
        var remove = P12SecondaryButton(_arabic ? "إزالة من المجموعة" : "Remove from collection");

        async Task MutateAsync(bool adding)
        {
            if (_p05CurationClient is null || combo.SelectedItem is not ComboBoxItem item || item.Tag is not CollectionSnapshot collection || !Guid.TryParse(assetId.Text.Trim(), out var id))
            {
                state.Text = _arabic ? "اختر مجموعة وأدخل معرّف أصل صحيحًا." : "Choose a collection and enter a valid asset GUID.";
                return;
            }
            add.IsEnabled = remove.IsEnabled = false;
            state.Text = _arabic ? "جاري تنفيذ العملية…" : "Applying collection membership change…";
            try
            {
                var updated = adding
                    ? await _p05CurationClient.AddToCollectionAsync(collection.CollectionId, id, collection.Version)
                    : await _p05CurationClient.RemoveFromCollectionAsync(collection.CollectionId, id, collection.Version);
                state.Text = _arabic ? $"تم التنفيذ · الإصدار {updated.Version}" : $"Completed · collection version {updated.Version}";
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { state.Text = _arabic ? "تعارض إصدار؛ أعد تحميل الصفحة." : "Version conflict; reload the action page."; }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { state.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { state.Text = _arabic ? "فشلت عملية عضوية المجموعة." : "Collection membership operation failed."; }
            finally { add.IsEnabled = remove.IsEnabled = true; }
        }
        add.Click += async (_, _) => await MutateAsync(true);
        remove.Click += async (_, _) => await MutateAsync(false);

        var row = new WrapPanel();
        row.Children.Add(combo);
        row.Children.Add(assetId);
        row.Children.Add(add);
        row.Children.Add(remove);
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(state);
        return Card(_arabic ? "عضوية المجموعات" : "Collection membership", stack);
    }

    private async Task ShowP12AdministrationActionsAsync()
    {
        if (_currentRoute != "admin-actions") return;
        if (_p08AdministrationClient is null)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إجراءات الإدارة" : "Management Actions", _arabic ? "واجهة API المركزية غير مهيأة." : "Central API is not configured."),
                StateCard("Degraded", _arabic ? "إجراءات الإدارة تحتاج الخدمة المركزية." : "Management actions require the Central API.", "#FFFAEB", "#B54708")));
            return;
        }

        try
        {
            var policiesTask = _p08AdministrationClient.ListPoliciesAsync();
            var usersTask = _p08AdministrationClient.ListUsersAsync();
            await Task.WhenAll(policiesTask, usersTask);
            if (_currentRoute != "admin-actions") return;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إجراءات الإدارة" : "Management Actions",
                    _arabic ? "كل وظيفة إدارية قابلة للتنفيذ لها زر واضح ونتيجة مرئية؛ الأسرار تبقى SecretRef فقط." : "Every user-operable administration mutation has an explicit control and visible result; secrets remain opaque SecretRef values."),
                BuildP12PolicyActions(await policiesTask),
                BuildP12UserActions(await usersTask),
                BuildP12DictionaryActions(),
                BuildP12AuditExportAction()));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP12ActionFailure("admin-actions", _arabic ? "صلاحية Administrator مطلوبة." : "Administrator permission is required.");
        }
        catch
        {
            ShowP12ActionFailure("admin-actions", _arabic ? "تعذر تحميل إجراءات الإدارة." : "Management actions could not be loaded.");
        }
    }

    private FrameworkElement BuildP12PolicyActions(IReadOnlyList<AdminPolicyRecord> policies)
    {
        var combo = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var policy in policies)
            combo.Items.Add(new ComboBoxItem { Content = _arabic && !string.IsNullOrWhiteSpace(policy.DisplayNameAr) ? policy.DisplayNameAr : policy.DisplayNameEn, Tag = policy });
        var payload = P12TextBox(string.Empty, 10000, true); payload.MinHeight = 120;
        var secretRef = P12TextBox(string.Empty, 500);
        var enabled = new CheckBox { Content = _arabic ? "مفعلة" : "Enabled", Margin = new Thickness(0, 8, 12, 0) };
        var restart = new CheckBox { Content = _arabic ? "يتطلب إعادة تشغيل" : "Requires restart", Margin = new Thickness(0, 8, 12, 0) };
        var state = P12StateText();

        AdminPolicyRecord? Selected() => combo.SelectedItem is ComboBoxItem item ? item.Tag as AdminPolicyRecord : null;
        void LoadSelected()
        {
            var policy = Selected();
            if (policy is null) return;
            payload.Text = JsonSerializer.Serialize(policy.Payload, new JsonSerializerOptions { WriteIndented = true });
            secretRef.Text = policy.SecretRef ?? string.Empty;
            enabled.IsChecked = policy.IsEnabled;
            restart.IsChecked = policy.RequiresRestart;
        }
        combo.SelectionChanged += (_, _) => LoadSelected();
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;

        AdminPolicyUpdateRequest BuildRequest(AdminPolicyRecord policy)
        {
            using var document = JsonDocument.Parse(payload.Text);
            return new AdminPolicyUpdateRequest(policy.Version, policy.Category, policy.DisplayNameEn, policy.DisplayNameAr,
                document.RootElement.Clone(), string.IsNullOrWhiteSpace(secretRef.Text) ? null : secretRef.Text.Trim(), restart.IsChecked == true, enabled.IsChecked == true);
        }

        var validate = P12SecondaryButton(_arabic ? "تحقق" : "Validate");
        var test = P12SecondaryButton(_arabic ? "اختبار المرجع" : "Test reference");
        var save = P12ActionButton(_arabic ? "حفظ السياسة" : "Save policy");
        validate.Click += async (_, _) =>
        {
            var policy = Selected(); if (policy is null || _p08AdministrationClient is null) return;
            try { var result = await _p08AdministrationClient.ValidatePolicyAsync(policy.PolicyKey, BuildRequest(policy)); state.Text = result.Valid ? (_arabic ? "السياسة صالحة." : "Policy is valid.") : string.Join(" · ", result.Errors); }
            catch (JsonException) { state.Text = _arabic ? "JSON غير صالح." : "Policy JSON is invalid."; }
            catch { state.Text = _arabic ? "فشل التحقق." : "Policy validation failed."; }
        };
        test.Click += async (_, _) =>
        {
            var policy = Selected(); if (policy is null || _p08AdministrationClient is null) return;
            try { var result = await _p08AdministrationClient.TestPolicyAsync(policy.PolicyKey); state.Text = $"{result.Code}: {result.Detail}"; }
            catch { state.Text = _arabic ? "فشل اختبار المرجع." : "Reference test failed."; }
        };
        save.Click += async (_, _) =>
        {
            var policy = Selected(); if (policy is null || _p08AdministrationClient is null) return;
            try { var saved = await _p08AdministrationClient.UpdatePolicyAsync(policy.PolicyKey, BuildRequest(policy)); state.Text = _arabic ? $"تم الحفظ · الإصدار {saved.Version}" : $"Saved · version {saved.Version}"; }
            catch (JsonException) { state.Text = _arabic ? "JSON غير صالح." : "Policy JSON is invalid."; }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { state.Text = _arabic ? "تعارض إصدار؛ أعد تحميل الصفحة." : "Version conflict; reload the action page."; }
            catch { state.Text = _arabic ? "فشل حفظ السياسة." : "Policy save failed."; }
        };

        var buttons = new WrapPanel(); buttons.Children.Add(validate); buttons.Children.Add(test); buttons.Children.Add(save);
        var flags = new WrapPanel(); flags.Children.Add(enabled); flags.Children.Add(restart);
        var stack = new StackPanel(); stack.Children.Add(combo); stack.Children.Add(payload); stack.Children.Add(secretRef); stack.Children.Add(flags); stack.Children.Add(buttons); stack.Children.Add(state);
        return Card(_arabic ? "السياسات المركزية" : "Policy actions", stack);
    }

    private FrameworkElement BuildP12UserActions(IReadOnlyList<AdminUserPolicyRecord> users)
    {
        var combo = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var user in users) combo.Items.Add(new ComboBoxItem { Content = $"{user.DisplayName} · {user.UserName}", Tag = user });
        var userId = P12TextBox(string.Empty, 60); var userName = P12TextBox(string.Empty, 200); var displayName = P12TextBox(string.Empty, 200); var external = P12TextBox(string.Empty, 300); var roles = P12TextBox(string.Empty, 500);
        var enabled = new CheckBox { Content = _arabic ? "مفعّل" : "Enabled", Margin = new Thickness(0, 8, 12, 0) };
        var state = P12StateText();
        AdminUserPolicyRecord? Selected() => combo.SelectedItem is ComboBoxItem item ? item.Tag as AdminUserPolicyRecord : null;
        void LoadSelected()
        {
            var user = Selected(); if (user is null) return;
            userId.Text = user.UserId.ToString("D"); userName.Text = user.UserName; displayName.Text = user.DisplayName; external.Text = user.ExternalSubject ?? string.Empty; roles.Text = string.Join(", ", user.Roles); enabled.IsChecked = user.IsEnabled;
        }
        combo.SelectionChanged += (_, _) => LoadSelected(); if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        var create = P12SecondaryButton(_arabic ? "سجل مستخدم جديد" : "New authorization record");
        create.Click += (_, _) => { combo.SelectedIndex = -1; userId.Text = Guid.NewGuid().ToString("D"); userName.Text = displayName.Text = external.Text = roles.Text = string.Empty; enabled.IsChecked = true; state.Text = _arabic ? "أدخل بيانات سجل الصلاحيات الجديد." : "Enter the new authorization record details."; };
        var save = P12ActionButton(_arabic ? "حفظ المستخدم والأدوار" : "Save user & roles");
        save.Click += async (_, _) =>
        {
            if (_p08AdministrationClient is null || !Guid.TryParse(userId.Text.Trim(), out var id) || string.IsNullOrWhiteSpace(userName.Text) || string.IsNullOrWhiteSpace(displayName.Text)) { state.Text = _arabic ? "معرّف واسم المستخدم والاسم المعروض مطلوبة." : "User ID, user name and display name are required."; return; }
            var selected = Selected();
            var expected = selected?.Version ?? 0;
            try
            {
                var saved = await _p08AdministrationClient.UpdateUserAsync(id, new AdminUserPolicyUpdateRequest(expected, userName.Text.Trim(), displayName.Text.Trim(), string.IsNullOrWhiteSpace(external.Text) ? null : external.Text.Trim(), enabled.IsChecked == true, roles.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                state.Text = _arabic ? $"تم الحفظ · الإصدار {saved.Version}" : $"Saved · version {saved.Version}";
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { state.Text = _arabic ? "تعارض إصدار؛ أعد تحميل الصفحة." : "Version conflict; reload the action page."; }
            catch { state.Text = _arabic ? "فشل حفظ المستخدم." : "User policy save failed."; }
        };
        var buttons = new WrapPanel(); buttons.Children.Add(create); buttons.Children.Add(save);
        var stack = new StackPanel(); stack.Children.Add(combo); stack.Children.Add(P12Label("User ID")); stack.Children.Add(userId); stack.Children.Add(P12Label(_arabic ? "اسم المستخدم" : "User name")); stack.Children.Add(userName); stack.Children.Add(P12Label(_arabic ? "الاسم المعروض" : "Display name")); stack.Children.Add(displayName); stack.Children.Add(P12Label("External subject")); stack.Children.Add(external); stack.Children.Add(P12Label(_arabic ? "الأدوار · مفصولة بفواصل" : "Roles · comma separated")); stack.Children.Add(roles); stack.Children.Add(enabled); stack.Children.Add(buttons); stack.Children.Add(state);
        return Card(_arabic ? "المستخدمون والأدوار" : "Users & roles", stack);
    }

    private FrameworkElement BuildP12DictionaryActions()
    {
        var dictionaryKey = P12TextBox("metadata.categories", 200); var entryKey = P12TextBox(string.Empty, 200); var labelEn = P12TextBox(string.Empty, 300); var labelAr = P12TextBox(string.Empty, 300); labelAr.FlowDirection = FlowDirection.RightToLeft;
        var enabled = new CheckBox { Content = _arabic ? "مفعّل" : "Enabled", IsChecked = true, Margin = new Thickness(0, 8, 12, 0) };
        var entries = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 6, 8, 0), Padding = new Thickness(8, 6, 8, 6) };
        var state = P12StateText();
        AdminDictionaryEntry? Selected() => entries.SelectedItem is ComboBoxItem item ? item.Tag as AdminDictionaryEntry : null;
        void LoadSelected() { var item = Selected(); if (item is null) return; entryKey.Text = item.EntryKey; labelEn.Text = item.LabelEn; labelAr.Text = item.LabelAr; enabled.IsChecked = item.IsEnabled; }
        entries.SelectionChanged += (_, _) => LoadSelected();
        var load = P12SecondaryButton(_arabic ? "تحميل القاموس" : "Load dictionary");
        load.Click += async (_, _) =>
        {
            if (_p08AdministrationClient is null || string.IsNullOrWhiteSpace(dictionaryKey.Text)) return;
            try
            {
                var result = await _p08AdministrationClient.ListDictionaryAsync(dictionaryKey.Text.Trim());
                entries.Items.Clear(); foreach (var item in result) entries.Items.Add(new ComboBoxItem { Content = $"{item.EntryKey} · {(_arabic ? item.LabelAr : item.LabelEn)}", Tag = item });
                if (entries.Items.Count > 0) entries.SelectedIndex = 0;
                state.Text = _arabic ? $"تم تحميل {result.Count} عنصر." : $"Loaded {result.Count} entries.";
            }
            catch { state.Text = _arabic ? "تعذر تحميل القاموس." : "Dictionary could not be loaded."; }
        };
        var create = P12SecondaryButton(_arabic ? "عنصر جديد" : "New entry");
        create.Click += (_, _) => { entries.SelectedIndex = -1; entryKey.Text = labelEn.Text = labelAr.Text = string.Empty; enabled.IsChecked = true; };
        var save = P12ActionButton(_arabic ? "حفظ عنصر القاموس" : "Save dictionary entry");
        save.Click += async (_, _) =>
        {
            if (_p08AdministrationClient is null || string.IsNullOrWhiteSpace(dictionaryKey.Text) || string.IsNullOrWhiteSpace(entryKey.Text) || string.IsNullOrWhiteSpace(labelEn.Text) || string.IsNullOrWhiteSpace(labelAr.Text)) { state.Text = _arabic ? "مفتاح القاموس والعنصر والتسميتان مطلوبة." : "Dictionary key, entry key and both labels are required."; return; }
            try
            {
                var selected = Selected();
                var saved = await _p08AdministrationClient.UpdateDictionaryEntryAsync(dictionaryKey.Text.Trim(), entryKey.Text.Trim(), new AdminDictionaryUpdateRequest(selected?.Version ?? 0, labelEn.Text.Trim(), labelAr.Text.Trim(), enabled.IsChecked == true));
                state.Text = _arabic ? $"تم الحفظ · الإصدار {saved.Version}" : $"Saved · version {saved.Version}";
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { state.Text = _arabic ? "تعارض إصدار؛ أعد تحميل القاموس." : "Version conflict; reload the dictionary."; }
            catch { state.Text = _arabic ? "فشل حفظ عنصر القاموس." : "Dictionary entry save failed."; }
        };
        var buttons = new WrapPanel(); buttons.Children.Add(load); buttons.Children.Add(create); buttons.Children.Add(save);
        var stack = new StackPanel(); stack.Children.Add(P12Label(_arabic ? "مفتاح القاموس" : "Dictionary key")); stack.Children.Add(dictionaryKey); stack.Children.Add(entries); stack.Children.Add(P12Label(_arabic ? "مفتاح العنصر" : "Entry key")); stack.Children.Add(entryKey); stack.Children.Add(P12Label(_arabic ? "التسمية الإنجليزية" : "English label")); stack.Children.Add(labelEn); stack.Children.Add(P12Label(_arabic ? "التسمية العربية" : "Arabic label")); stack.Children.Add(labelAr); stack.Children.Add(enabled); stack.Children.Add(buttons); stack.Children.Add(state);
        return Card(_arabic ? "القواميس الثنائية اللغة" : "Bilingual dictionaries", stack);
    }

    private FrameworkElement BuildP12AuditExportAction()
    {
        var state = P12StateText();
        var export = P12ActionButton(_arabic ? "تصدير سجل التدقيق CSV" : "Export audit CSV");
        export.Click += async (_, _) =>
        {
            if (_p08AdministrationClient is null) return;
            try
            {
                var csv = await _p08AdministrationClient.ExportAuditCsvAsync(new AdminAuditQuery(Limit: 500));
                var dialog = new SaveFileDialog { FileName = $"mam-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv", Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
                if (dialog.ShowDialog(this) != true) { state.Text = _arabic ? "تم إلغاء الحفظ." : "Save cancelled."; return; }
                await File.WriteAllTextAsync(dialog.FileName, csv, System.Text.Encoding.UTF8);
                state.Text = _arabic ? "تم حفظ ملف التدقيق." : "Audit CSV saved.";
            }
            catch { state.Text = _arabic ? "فشل تصدير سجل التدقيق." : "Audit export failed."; }
        };
        var stack = new StackPanel(); stack.Children.Add(export); stack.Children.Add(state);
        return Card(_arabic ? "تصدير التدقيق" : "Audit export", stack);
    }

    private TextBox P12TextBox(string value, int maxLength, bool multiline = false) => new()
    {
        Text = value,
        MaxLength = maxLength,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 6, 8, 0)
    };

    private TextBlock P12Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Navy() };
    private TextBlock P12StateText() => new() { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
    private static Button P12ActionButton(string text) => new() { Content = text, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 8, 10, 0), Background = Gold(), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
    private static Button P12SecondaryButton(string text) => new() { Content = text, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 8, 10, 0), Background = System.Windows.Media.Brushes.White, Foreground = Navy(), BorderBrush = Gold(), BorderThickness = new Thickness(1) };

    private void ShowP12ActionFailure(string route, string detail)
    {
        if (_currentRoute != route) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? _titles[route].Ar : _titles[route].En, detail),
            StateCard("API error", detail, "#FEF3F2", "#B42318")));
    }
}
