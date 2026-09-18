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
    private string _t21Query = string.Empty;

    private void InitializeT21TapeInventoryIntegration()
    {
        if (_t21TapeClient is not null || _p02HttpClient is null) return;

        _t21TapeClient = new MamTapeInventoryApiClient(
            _p02HttpClient,
            "WindowsDesktop",
            Environment.GetEnvironmentVariable("MAM_DEV_USER"));

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button => string.Equals(button.Tag as string, "capture", StringComparison.OrdinalIgnoreCase)))
        {
            button.Content = _arabic ? "فهرس الشرائط" : "Tape Inventory";
            button.Click += T21TapeNavigate_Click;
        }

        LanguageButton.Click += T21LanguageChanged_Click;
    }

    private async void T21TapeNavigate_Click(object sender, RoutedEventArgs e)
    {
        _currentRoute = "capture"; // legacy shell route; Phase Two replaces its old capture workspace with inventory.
        PageTitle.Text = _arabic ? "فهرس الشرائط" : "Tape Inventory";
        await LoadT21TapeInventoryAsync();
    }

    private async void T21LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in NavPanel.Children.OfType<Button>().Where(button => string.Equals(button.Tag as string, "capture", StringComparison.OrdinalIgnoreCase)))
            button.Content = _arabic ? "فهرس الشرائط" : "Tape Inventory";

        if (string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase))
        {
            PageTitle.Text = _arabic ? "فهرس الشرائط" : "Tape Inventory";
            await LoadT21TapeInventoryAsync();
        }
    }

    private async Task LoadT21TapeInventoryAsync()
    {
        if (_t21TapeClient is null) return;

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "فهرس الشرائط" : "Tape Inventory",
                _arabic ? "فهرسة الشرائط وبياناتها فقط. تتم عملية التحويل الرقمي خارج نظام MAM." : "Physical tape catalog and metadata only. Digitization is performed outside MAM."),
            StateCard("Loading", _arabic ? "جاري تحميل فهرس الشرائط المركزي…" : "Loading the authoritative tape inventory…", "#EFF8FF", "#175CD3")));

        try
        {
            if (_t21Formats.Count == 0)
                _t21Formats = await _t21TapeClient.ListFormatsAsync();
            var page = await _t21TapeClient.ListAsync(_t21Query, 250);
            if (!string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase)) return;

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
                Lead(_arabic ? "فهرس الشرائط" : "Tape Inventory",
                    _arabic ? $"{page.Total} سجل · بيانات مركزية مباشرة" : $"{page.Total} records · live authoritative data"),
                stack));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowT21State("Permission denied", _arabic ? "لا توجد صلاحية لقراءة فهرس الشرائط." : "The current identity cannot read tape inventory.", "#FFF6ED", "#C4320A");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowT21State("API error", _arabic ? "تعذر الوصول إلى خدمة فهرس الشرائط." : "Tape inventory service is unreachable.", "#FEF3F2", "#B42318");
        }
    }

    private FrameworkElement BuildT21Toolbar()
    {
        var query = new TextBox
        {
            Text = _t21Query,
            MinWidth = 300,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = _arabic ? "كود الشريط أو الرقم القديم أو العنوان أو الموقع" : "Tape code, legacy number, title or location"
        };
        var search = new Button
        {
            Content = _arabic ? "بحث" : "Search",
            Padding = new Thickness(16, 9, 16, 9),
            Background = Gold(),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        search.Click += async (_, _) =>
        {
            _t21Query = query.Text.Trim();
            await LoadT21TapeInventoryAsync();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(query);
        row.Children.Add(search);
        return Card(string.Empty, row);
    }

    private FrameworkElement BuildT21CreateCard()
    {
        var title = T21Input(_arabic ? "العنوان (اختياري)" : "Title (optional)");
        var legacy = T21Input(_arabic ? "الرقم القديم" : "Legacy number");
        var format = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6) };
        format.Items.Add(new ComboBoxItem { Content = _arabic ? "غير معروف" : "Unknown", Tag = string.Empty, IsSelected = true });
        foreach (var item in _t21Formats)
            format.Items.Add(new ComboBoxItem { Content = _arabic ? item.NameAr : item.NameEn, Tag = item.Code });
        var room = T21Input(_arabic ? "الغرفة" : "Room");
        var shelf = T21Input(_arabic ? "الرف" : "Shelf");
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
            create.IsEnabled = false;
            status.Text = _arabic ? "جاري تخصيص كود الشريط وحفظ السجل…" : "Allocating tape code and saving…";
            try
            {
                var formatCode = (format.SelectedItem as ComboBoxItem)?.Tag as string;
                await _t21TapeClient.CreateAsync(new CreateTapeRequest(
                    T21Null(legacy.Text), T21Null(title.Text), null, T21Null(formatCode), null, null,
                    null, null, T21Null(room.Text), null, T21Null(shelf.Text), null, null));
                await LoadT21TapeInventoryAsync();
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                status.Text = _arabic ? "لا توجد صلاحية لإنشاء سجلات الشرائط." : "Permission denied for tape creation.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
            {
                status.Text = _arabic ? "تعذر حفظ سجل الشريط." : "Tape record could not be saved.";
            }
            finally { create.IsEnabled = true; }
        };

        var fields = new WrapPanel();
        fields.Children.Add(title); fields.Children.Add(legacy); fields.Children.Add(format); fields.Children.Add(room); fields.Children.Add(shelf);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = _arabic ? "إضافة شريط فعلي" : "Register physical tape", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Navy() });
        body.Children.Add(new TextBlock { Text = _arabic ? "يتم تخصيص TAPE-###### عند الحفظ الناجح فقط." : "TAPE-###### is allocated only on successful save.", Margin = new Thickness(0, 5, 0, 0), Foreground = Text() });
        body.Children.Add(fields); body.Children.Add(create); body.Children.Add(status);
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
        return Card(string.Empty, details);
    }

    private FrameworkElement BuildT21EditCard(TapeInventoryItem tape)
    {
        var title = T21Input(_arabic ? "العنوان" : "Title", tape.Title);
        var legacy = T21Input(_arabic ? "الرقم القديم" : "Legacy number", tape.LegacyNumber);
        var room = T21Input(_arabic ? "الغرفة" : "Room", tape.Room);
        var cabinet = T21Input(_arabic ? "الخزانة" : "Cabinet", tape.Cabinet);
        var shelf = T21Input(_arabic ? "الرف" : "Shelf", tape.Shelf);
        var bin = T21Input(_arabic ? "الصندوق" : "Bin", tape.Bin);
        var condition = T21Input(_arabic ? "الحالة المادية" : "Physical condition", tape.PhysicalCondition);
        var statusCombo = new ComboBox { MinWidth = 210, Margin = new Thickness(0, 8, 10, 0), Padding = new Thickness(8, 6, 8, 6) };
        foreach (var value in TapeDigitizationStatuses.All)
            statusCombo.Items.Add(new ComboBoxItem { Content = value, Tag = value, IsSelected = string.Equals(value, tape.DigitizationStatus, StringComparison.Ordinal) });
        var state = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() };
        var save = new Button { Content = _arabic ? "حفظ" : "Save", Margin = new Thickness(0, 10, 8, 0), Padding = new Thickness(16, 9, 16, 9), Background = Gold(), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var cancel = new Button { Content = _arabic ? "رجوع" : "Back", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(16, 9, 16, 9), Background = Brushes.White, Foreground = Navy(), BorderBrush = Brush("#D0D5DD") };
        save.Click += async (_, _) =>
        {
            if (_t21TapeClient is null) return;
            save.IsEnabled = false;
            try
            {
                var selectedStatus = (statusCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? tape.DigitizationStatus;
                await _t21TapeClient.UpdateAsync(tape.TapeId, new UpdateTapeRequest(
                    T21Null(legacy.Text), T21Null(title.Text), tape.Description, tape.TapeFormatCode, T21Null(condition.Text), selectedStatus,
                    tape.OwnerDepartment, tape.DurationSeconds, tape.RecordingDate, T21Null(room.Text), T21Null(cabinet.Text), T21Null(shelf.Text), T21Null(bin.Text), tape.Notes, tape.Version));
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
                state.Text = _arabic ? "تعذر حفظ التعديل." : "Tape update failed.";
            }
            finally { save.IsEnabled = true; }
        };
        cancel.Click += async (_, _) => await LoadT21TapeInventoryAsync();

        var fields = new WrapPanel();
        foreach (var element in new FrameworkElement[] { title, legacy, condition, room, cabinet, shelf, bin, statusCombo }) fields.Children.Add(element);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(save); buttons.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(fields); body.Children.Add(buttons); body.Children.Add(state);
        return Card(string.Empty, body);
    }

    private static TextBox T21Input(string tooltip, string? value = null) => new()
    {
        Text = value ?? string.Empty,
        MinWidth = 180,
        Margin = new Thickness(0, 8, 10, 0),
        Padding = new Thickness(10, 8, 10, 8),
        ToolTip = tooltip
    };

    private static string? T21Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string T21Location(TapeInventoryItem tape) => string.Join("/", new[] { tape.Room, tape.Cabinet, tape.Shelf, tape.Bin }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private void ShowT21State(string title, string detail, string background, string foreground)
    {
        if (!string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "فهرس الشرائط" : "Tape Inventory", _arabic ? "حالة الاتصال بالخدمة المركزية." : "Central tape inventory connection state."),
            StateCard(title, detail, background, foreground)));
    }
}
