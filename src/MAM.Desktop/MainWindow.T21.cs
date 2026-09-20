using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Clients;
using MAM.Application.Tapes;
using MAM.Domain.Tapes;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamTapeInventoryApiClient? _t21TapeClient;
    private IReadOnlyList<TapeFormatItem> _t21Formats = Array.Empty<TapeFormatItem>();
    private IReadOnlyList<TapeDepartmentItem> _t21Departments = Array.Empty<TapeDepartmentItem>();
    private string _t21Query = string.Empty;

    private void InitializeT21TapeInventoryIntegration()
    {
        if (_t21TapeClient is not null || _p02HttpClient is null) return;

        _t21TapeClient = new MamTapeInventoryApiClient(
            _p02HttpClient,
            "WindowsDesktop",
            DesktopProductionTransport.DevelopmentUser);

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button => string.Equals(button.Tag as string, "tapes", StringComparison.OrdinalIgnoreCase)))
        {
            button.Content = _arabic ? "إدارة الشرائط" : "Tape Inventory";
            button.Click += T21TapeNavigate_Click;
        }

        LanguageButton.Click += T21LanguageChanged_Click;
    }

    private async void T21TapeNavigate_Click(object sender, RoutedEventArgs e)
    {
        _currentRoute = "tapes";
        PageTitle.Text = _arabic ? "إدارة الشرائط" : "Tape Inventory";
        await LoadT21TapeInventoryAsync();
    }

    private async void T21LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in NavPanel.Children.OfType<Button>().Where(button => string.Equals(button.Tag as string, "tapes", StringComparison.OrdinalIgnoreCase)))
            button.Content = _arabic ? "إدارة الشرائط" : "Tape Inventory";

        if (string.Equals(_currentRoute, "tapes", StringComparison.OrdinalIgnoreCase))
        {
            PageTitle.Text = _arabic ? "إدارة الشرائط" : "Tape Inventory";
            await LoadT21TapeInventoryAsync();
        }
    }

    private async Task LoadT21TapeInventoryAsync()
    {
        if (_t21TapeClient is null) return;

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "إدارة الشرائط" : "Tape Inventory",
                _arabic ? "فهرسة الشرائط وبياناتها فقط. تتم عملية التحويل الرقمي خارج نظام MAM." : "Physical tape catalog and metadata only. Digitization is performed outside MAM."),
            StateCard("Loading", _arabic ? "جاري تحميل إدارة الشرائط المركزي…" : "Loading the authoritative tape inventory…", "#EFF8FF", "#175CD3")));

        try
        {
            if (_t21Formats.Count == 0)
                _t21Formats = await _t21TapeClient.ListFormatsAsync();
            if (_t21Departments.Count == 0)
                _t21Departments = await _t21TapeClient.ListDepartmentsAsync();
            var page = await _t21TapeClient.ListAsync(_t21Query, 250);
            if (!string.Equals(_currentRoute, "tapes", StringComparison.OrdinalIgnoreCase)) return;

            var stack = new StackPanel();
            stack.Children.Add(BuildT21Toolbar());
            stack.Children.Add(BuildT21CreateCard());

            if (page.Items.Count == 0)
            {
                stack.Children.Add(StateCard("Empty", _arabic ? "لا توجد شرائط مطابقة." : "No tapes match the current inventory query.", "#F9FAFB", "#475467"));
            }
            else
            {
                foreach (var tape in page.Items)
                    stack.Children.Add(BuildT21TapeRow(tape));
            }

            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "إدارة الشرائط" : "Tape Inventory",
                    _arabic ? $"{page.Total} سجل · بيانات مركزية مباشرة" : $"{page.Total} records · live authoritative data"),
                stack));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowT21State("Permission denied", _arabic ? "لا توجد صلاحية لقراءة إدارة الشرائط." : "The current identity cannot read tape inventory.", "#FFF6ED", "#C4320A");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowT21State("API error", _arabic ? "تعذر الوصول إلى خدمة إدارة الشرائط." : "Tape inventory service is unreachable.", "#FEF3F2", "#B42318");
        }
    }

    private FrameworkElement BuildT21Toolbar()
    {
        var query = new TextBox
        {
            Text = _t21Query,
            MinWidth = 280,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = _arabic ? "بحث نصي: الكود أو الاسم أو الرقم القديم أو الإدارة أو النوع أو الحالة أو الموقع أو الملاحظات" : "Text search: code, name, legacy number, department, format, status, location or notes"
        };
        var search = new Button
        {
            Content = _arabic ? "بحث" : "Search",
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(0,0,18,0),
            Background = Gold(), Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        search.Click += async (_, _) => { _t21Query = query.Text.Trim(); await LoadT21TapeInventoryAsync(); };

        var scan = new TextBox
        {
            MinWidth = 300,
            Padding = new Thickness(10,8,10,8),
            Margin = new Thickness(0,0,10,0),
            ToolTip = _arabic ? "امسح باركود الشريط أو الصق القيمة كاملة" : "Scan a tape barcode or paste its full value"
        };
        var scanButton = new Button
        {
            Content = _arabic ? "فتح من الباركود" : "Open barcode",
            Padding = new Thickness(16,9,16,9),
            Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD")
        };
        scanButton.Click += async (_, _) => await T21ResolveScanAsync(scan.Text);
        scan.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await T21ResolveScanAsync(scan.Text); };

        var row = new WrapPanel();
        row.Children.Add(query); row.Children.Add(search); row.Children.Add(scan); row.Children.Add(scanButton);
        return Card(string.Empty, row);
    }

    private FrameworkElement BuildT21CreateCard()
    {
        var title = T21Input(_arabic ? "العنوان (اختياري)" : "Title (optional)");
        var legacy = T21Input(_arabic ? "الرقم القديم" : "Legacy number");
        var description = T21Input(_arabic ? "الوصف" : "Description", null, true);
        var owner = T21DepartmentCombo(null);
        var duration = T21Input(_arabic ? "المدة بالثواني" : "Duration (seconds)");
        var room = T21Input(_arabic ? "الغرفة" : "Room");
        var cabinet = T21Input(_arabic ? "الخزانة" : "Cabinet");
        var shelf = T21Input(_arabic ? "الرف" : "Shelf");
        var bin = T21Input(_arabic ? "الصندوق" : "Bin");
        var notes = T21Input(_arabic ? "ملاحظات" : "Notes", null, true);
        var recordingDate = new DatePicker { MinWidth = 180, Margin = new Thickness(0, 8, 10, 0), ToolTip = _arabic ? "تاريخ التسجيل" : "Recording date" };
        var format = T21FormatCombo(null);
        var condition = T21ConditionCombo(null);
        var status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
        var create = new Button
        {
            Content = _arabic ? "إنشاء الشريط" : "Create tape",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(16, 9, 16, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Gold(), Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        create.Click += async (_, _) =>
        {
            if (_t21TapeClient is null) return;
            if (!T21TryOptionalInt(duration.Text, out var durationSeconds))
            {
                status.Text = _arabic ? "المدة يجب أن تكون رقمًا صحيحًا موجبًا." : "Duration must be a non-negative whole number.";
                return;
            }
            create.IsEnabled = false;
            status.Text = _arabic ? "جاري تخصيص كود الشريط وحفظ السجل…" : "Allocating tape code and saving…";
            try
            {
                var formatCode = (format.SelectedItem as ComboBoxItem)?.Tag as string;
                await _t21TapeClient.CreateAsync(new CreateTapeRequest(
                    T21Null(legacy.Text), T21Null(title.Text), T21Null(description.Text), T21Null(formatCode),
                    (condition.SelectedItem as ComboBoxItem)?.Tag as string, T21Null((owner.SelectedItem as ComboBoxItem)?.Tag as string), durationSeconds,
                    recordingDate.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null,
                    T21Null(room.Text), T21Null(cabinet.Text), T21Null(shelf.Text), T21Null(bin.Text), T21Null(notes.Text)));
                await LoadT21TapeInventoryAsync();
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                status.Text = _arabic ? "لا توجد صلاحية لإنشاء سجلات الشرائط." : "Permission denied for tape creation.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                status.Text = _arabic ? $"تعذر حفظ سجل الشريط: {ex.Message}" : $"Tape record could not be saved: {ex.Message}";
            }
            finally { create.IsEnabled = true; }
        };

        var fields = new WrapPanel();
        foreach (var element in new FrameworkElement[] { title, legacy, format, condition, owner, duration, recordingDate, room, cabinet, shelf, bin })
            fields.Children.Add(element);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = _arabic ? "إضافة شريط فعلي" : "Register physical tape", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Navy() });
        body.Children.Add(new TextBlock { Text = _arabic ? "إدارة السجل فقط؛ لا يوجد تسجيل مباشر من الشريط داخل MAM." : "Inventory management only; direct tape recording is not part of MAM.", Margin = new Thickness(0, 5, 0, 0), Foreground = Text() });
        body.Children.Add(fields);
        body.Children.Add(description);
        body.Children.Add(notes);
        body.Children.Add(create);
        body.Children.Add(status);
        return Card(string.Empty, body);
    }

    private FrameworkElement BuildT21TapeRow(TapeInventoryItem tape)
    {
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = tape.TapeCode, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Navy() });
        details.Children.Add(new TextBlock { Text = tape.Title ?? (_arabic ? "بدون عنوان" : "Untitled"), Margin = new Thickness(0, 3, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Text() });
        details.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", new[] { tape.TapeFormatCode, tape.LegacyNumber, T21Location(tape), tape.PhysicalCondition, tape.DigitizationStatus }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Margin = new Thickness(0, 4, 0, 0), Foreground = Text(), TextWrapping = TextWrapping.Wrap
        });

        var edit = new Button
        {
            Content = _arabic ? "تعديل" : "Edit",
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 7, 14, 7), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD")
        };
        edit.Click += (_, _) => ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "تعديل سجل الشريط" : "Edit tape record", tape.TapeCode),
            BuildT21EditCard(tape)));
        details.Children.Add(edit);

        var printBarcode = new Button
        {
            Content = _arabic ? "طباعة الباركود" : "Print barcode",
            Margin = new Thickness(8, 10, 0, 0), Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD")
        };
        printBarcode.Click += async (_, _) => await T21PrintBarcodeAsync(tape);
        details.Children.Add(printBarcode);

        var report = new Button
        {
            Content = _arabic ? "تقرير الشريط" : "Tape report",
            Margin = new Thickness(8, 10, 0, 0), Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD")
        };
        report.Click += async (_, _) => await T21PrintReportAsync(tape);
        details.Children.Add(report);
        return Card(string.Empty, details);
    }

    private FrameworkElement BuildT21EditCard(TapeInventoryItem tape)
    {
        var title = T21Input(_arabic ? "العنوان" : "Title", tape.Title);
        var legacy = T21Input(_arabic ? "الرقم القديم" : "Legacy number", tape.LegacyNumber);
        var description = T21Input(_arabic ? "الوصف" : "Description", tape.Description, true);
        var owner = T21DepartmentCombo(tape.OwnerDepartment);
        var duration = T21Input(_arabic ? "المدة بالثواني" : "Duration (seconds)", tape.DurationSeconds?.ToString());
        var recordingDate = new DatePicker { MinWidth = 180, Margin = new Thickness(0, 8, 10, 0), ToolTip = _arabic ? "تاريخ التسجيل" : "Recording date", SelectedDate = tape.RecordingDate?.ToDateTime(TimeOnly.MinValue) };
        var room = T21Input(_arabic ? "الغرفة" : "Room", tape.Room);
        var cabinet = T21Input(_arabic ? "الخزانة" : "Cabinet", tape.Cabinet);
        var shelf = T21Input(_arabic ? "الرف" : "Shelf", tape.Shelf);
        var bin = T21Input(_arabic ? "الصندوق" : "Bin", tape.Bin);
        var notes = T21Input(_arabic ? "ملاحظات" : "Notes", tape.Notes, true);
        var condition = T21ConditionCombo(tape.PhysicalCondition);
        var format = T21FormatCombo(tape.TapeFormatCode);
        var statusCombo = new ComboBox { MinWidth = 210, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var value in TapeDigitizationStatuses.All)
            statusCombo.Items.Add(new ComboBoxItem { Content = value, Tag = value, IsSelected = string.Equals(value, tape.DigitizationStatus, StringComparison.Ordinal) });
        var state = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
        var save = new Button { Content = _arabic ? "حفظ" : "Save", Margin = new Thickness(0, 10, 8, 0), Padding = new Thickness(16, 9, 16, 9), Background = Gold(), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var delete = new Button { Content = _arabic ? "حذف الشريط" : "Delete tape", Margin = new Thickness(0, 10, 8, 0), Padding = new Thickness(16, 9, 16, 9), Background = Brush("#B42318"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var cancel = new Button { Content = _arabic ? "رجوع" : "Back", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(16, 9, 16, 9), Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD") };
        save.Click += async (_, _) =>
        {
            if (_t21TapeClient is null) return;
            if (!T21TryOptionalInt(duration.Text, out var durationSeconds))
            {
                state.Text = _arabic ? "المدة يجب أن تكون رقمًا صحيحًا موجبًا." : "Duration must be a non-negative whole number.";
                return;
            }
            save.IsEnabled = false;
            try
            {
                var selectedStatus = (statusCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? tape.DigitizationStatus;
                var formatCode = (format.SelectedItem as ComboBoxItem)?.Tag as string;
                await _t21TapeClient.UpdateAsync(tape.TapeId, new UpdateTapeRequest(
                    T21Null(legacy.Text), T21Null(title.Text), T21Null(description.Text), T21Null(formatCode),
                    (condition.SelectedItem as ComboBoxItem)?.Tag as string, selectedStatus, T21Null((owner.SelectedItem as ComboBoxItem)?.Tag as string), durationSeconds,
                    recordingDate.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null,
                    T21Null(room.Text), T21Null(cabinet.Text), T21Null(shelf.Text), T21Null(bin.Text), T21Null(notes.Text), tape.Version));
                await LoadT21TapeInventoryAsync();
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                state.Text = _arabic ? "تم تعديل السجل بواسطة مستخدم آخر. ارجع وافتح السجل مرة أخرى." : "Another user changed this record. Go back and reopen it.";
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                state.Text = _arabic ? "لا توجد صلاحية للتعديل." : "Permission denied for tape update.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                state.Text = _arabic ? $"تعذر حفظ التعديل: {ex.Message}" : $"Tape update failed: {ex.Message}";
            }
            finally { save.IsEnabled = true; }
        };
        delete.Click += async (_, _) =>
        {
            if (_t21TapeClient is null) return;
            var message = _arabic
                ? $"سيتم حذف سجل {tape.TapeCode} نهائيًا.\nلن يتم حذف أي ملف ميديا لأن التسجيل المباشر غير مستخدم."
                : $"Tape record {tape.TapeCode} will be deleted permanently.\nNo media file is deleted because direct tape recording is not used.";
            if (MessageBox.Show(this, message, _arabic ? "تأكيد حذف الشريط" : "Confirm tape deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            delete.IsEnabled = false;
            try
            {
                await _t21TapeClient.DeleteAsync(tape.TapeId, tape.Version);
                await LoadT21TapeInventoryAsync();
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                state.Text = _arabic ? "تم تعديل السجل بواسطة مستخدم آخر. أعد فتحه قبل الحذف." : "Another user changed this record. Reopen it before deleting.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                state.Text = _arabic ? $"تعذر حذف الشريط: {ex.Message}" : $"Tape deletion failed: {ex.Message}";
            }
            finally { delete.IsEnabled = true; }
        };
        cancel.Click += async (_, _) => await LoadT21TapeInventoryAsync();

        var fields = new WrapPanel();
        foreach (var element in new FrameworkElement[] { title, legacy, format, condition, owner, duration, recordingDate, room, cabinet, shelf, bin, statusCombo })
            fields.Children.Add(element);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        var body = new StackPanel();
        body.Children.Add(fields);
        body.Children.Add(description);
        body.Children.Add(notes);
        body.Children.Add(buttons);
        body.Children.Add(state);
        return Card(string.Empty, body);
    }

    private ComboBox T21FormatCombo(string? selectedCode)
    {
        var combo = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6) };
        combo.Items.Add(new ComboBoxItem { Content = _arabic ? "غير معروف" : "Unknown", Tag = string.Empty, IsSelected = string.IsNullOrWhiteSpace(selectedCode) });
        var found = false;
        foreach (var item in _t21Formats)
        {
            var selected = string.Equals(item.Code, selectedCode, StringComparison.OrdinalIgnoreCase);
            found |= selected;
            combo.Items.Add(new ComboBoxItem { Content = _arabic ? item.NameAr : item.NameEn, Tag = item.Code, IsSelected = selected });
        }
        if (!found && !string.IsNullOrWhiteSpace(selectedCode))
            combo.Items.Add(new ComboBoxItem { Content = selectedCode, Tag = selectedCode, IsSelected = true });
        return combo;
    }

    private ComboBox T21DepartmentCombo(string? selectedCode)
    {
        var combo = new ComboBox { MinWidth = 210, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6), ToolTip = _arabic ? "الإدارة" : "Department" };
        combo.Items.Add(new ComboBoxItem { Content = _arabic ? "غير محدد" : "Not set", Tag = string.Empty, IsSelected = string.IsNullOrWhiteSpace(selectedCode) });
        var found = false;
        foreach (var item in _t21Departments.Where(x => x.IsActive || string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase)))
        {
            var selected = string.Equals(item.Code, selectedCode, StringComparison.OrdinalIgnoreCase);
            found |= selected;
            combo.Items.Add(new ComboBoxItem { Content = _arabic ? item.NameAr : item.NameEn, Tag = item.Code, IsSelected = selected });
        }
        if (!found && !string.IsNullOrWhiteSpace(selectedCode))
            combo.Items.Add(new ComboBoxItem { Content = selectedCode, Tag = selectedCode, IsSelected = true });
        return combo;
    }

    private async Task T21ResolveScanAsync(string value)
    {
        if (_t21TapeClient is null || string.IsNullOrWhiteSpace(value)) return;
        try
        {
            var tape = await _t21TapeClient.ResolveAsync(value.Trim());
            _t21Query = tape.TapeCode;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "نتيجة مسح الباركود" : "Barcode scan result", tape.TapeCode),
                BuildT21TapeRow(tape)));
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowT21State("Scan failed", _arabic ? $"تعذر العثور على الشريط: {ex.Message}" : $"Tape scan could not be resolved: {ex.Message}", "#FEF3F2", "#B42318");
        }
    }

    private ComboBox T21ConditionCombo(string? selectedValue)
    {
        var combo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6), ToolTip = _arabic ? "الحالة المادية" : "Physical condition" };
        foreach (var value in new[] { string.Empty, "Good", "Fair", "Poor", "Damaged" })
            combo.Items.Add(new ComboBoxItem { Content = string.IsNullOrEmpty(value) ? (_arabic ? "غير معروف" : "Unknown") : value, Tag = value, IsSelected = string.Equals(value, selectedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) });
        return combo;
    }

    private static TextBox T21Input(string tooltip, string? value = null, bool multiline = false) => new()
    {
        Text = value ?? string.Empty,
        MinWidth = multiline ? 520 : 180,
        MinHeight = multiline ? 72 : 0,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Margin = new Thickness(0, 8, 10, 0),
        Padding = new Thickness(10, 8, 10, 8),
        ToolTip = tooltip
    };

    private static bool T21TryOptionalInt(string? value, out int? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!int.TryParse(value.Trim(), out var number) || number < 0) return false;
        parsed = number;
        return true;
    }

    private static string? T21Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string T21Location(TapeInventoryItem tape) => string.Join("/", new[] { tape.Room, tape.Cabinet, tape.Shelf, tape.Bin }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private void ShowT21State(string title, string detail, string background, string foreground)
    {
        if (!string.Equals(_currentRoute, "tapes", StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "إدارة الشرائط" : "Tape Inventory", _arabic ? "حالة الاتصال بالخدمة المركزية." : "Central tape inventory connection state."),
            StateCard(title, detail, background, foreground)));
    }
}
