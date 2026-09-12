using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Clients;
using MAM.Application.Protection;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamProtectionApiClient? _protectionClient;
    private bool _p06Wired;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P06WindowLoaded));
    }

    private static void P06WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP06ProtectionUi();
    }

    private void WireP06ProtectionUi()
    {
        if (_p06Wired) return;
        _p06Wired = true;
        _titles["protection"] = ("Backup Protection", "حماية النسخة الاحتياطية");

        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
        {
            var http = new HttpClient
            {
                BaseAddress = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _protectionClient = new MamProtectionApiClient(http, "WindowsDesktop", Environment.GetEnvironmentVariable("MAM_DEV_USER"));
        }

        var button = new Button
        {
            Tag = "protection",
            Content = _arabic ? _titles["protection"].Ar : _titles["protection"].En,
            Style = (Style)FindResource("NavButton")
        };
        button.Click += async (_, _) =>
        {
            _currentRoute = "protection";
            PageTitle.Text = _arabic ? _titles["protection"].Ar : _titles["protection"].En;
            await ShowProtectionAdministrationAsync();
        };
        var admin = NavPanel.Children.OfType<Button>().FirstOrDefault(x => string.Equals(x.Tag as string, "admin", StringComparison.OrdinalIgnoreCase));
        var index = admin is null ? NavPanel.Children.Count : NavPanel.Children.IndexOf(admin);
        NavPanel.Children.Insert(index, button);

        LanguageButton.Click += async (_, _) =>
        {
            if (!string.Equals(_currentRoute, "protection", StringComparison.OrdinalIgnoreCase)) return;
            button.Content = _arabic ? _titles["protection"].Ar : _titles["protection"].En;
            await Task.Yield();
            await ShowProtectionAdministrationAsync();
        };
    }

    private async Task ShowProtectionAdministrationAsync()
    {
        if (_currentRoute != "protection") return;
        PageTitle.Text = _arabic ? "حماية النسخة الاحتياطية" : "Backup Protection";
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection",
                _arabic ? "جاري تحميل حالة الحماية من الخدمة المركزية…" : "Loading authoritative protection state from the Central API…"),
            StateCard("Loading", _arabic ? "جاري تحميل Primary و Backup…" : "Loading Primary and Backup health…", "#EFF8FF", "#175CD3")));

        if (_protectionClient is null)
        {
            ContentHost.Content = BuildProtectionUnavailable(_arabic ? "واجهة API المركزية غير مهيأة." : "Central API is not configured.");
            return;
        }

        try
        {
            var summaryTask = _protectionClient.GetSummaryAsync();
            var healthTask = _protectionClient.GetHealthAsync();
            await Task.WhenAll(summaryTask, healthTask);
            if (_currentRoute != "protection") return;
            ContentHost.Content = BuildProtectionAdmin(await summaryTask, await healthTask);
        }
        catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection", _arabic ? "الصلاحية مطلوبة." : "Authorization is required."),
                StateCard("Permission denied", _arabic ? "لا توجد صلاحية لإدارة الحماية." : "The current identity cannot administer protection.", "#FFF6ED", "#C4320A")));
        }
        catch
        {
            ContentHost.Content = BuildProtectionUnavailable(_arabic ? "تعذر الوصول إلى خدمة الحماية." : "Protection service is unavailable.");
        }
    }

    private FrameworkElement BuildProtectionAdmin(BackupProtectionSummary summary, BackupProtectionHealth health)
    {
        var actions = new WrapPanel();
        var queue = ActionButton(_arabic ? "إضافة النسخ المعلقة" : "Queue pending copies");
        var recheck = ActionButton(_arabic ? "إعادة فحص السلامة" : "Queue integrity recheck");
        var actionState = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = Text(), TextWrapping = TextWrapping.Wrap };
        queue.Click += async (_, _) =>
        {
            try
            {
                actionState.Text = _arabic ? "جاري إضافة النسخ المعلقة…" : "Queueing pending copies…";
                var queued = await _protectionClient!.QueueAsync();
                actionState.Text = _arabic ? $"تمت إضافة {queued} مهمة حماية." : $"Queued {queued} protection item(s).";
            }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { actionState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { actionState.Text = _arabic ? "فشل الطلب." : "Operation failed."; }
        };
        recheck.Click += async (_, _) =>
        {
            try
            {
                actionState.Text = _arabic ? "جاري إضافة فحوصات السلامة…" : "Queueing integrity rechecks…";
                var queued = await _protectionClient!.QueueIntegrityRecheckAsync(24);
                actionState.Text = _arabic ? $"تمت إضافة {queued} عملية إعادة فحص." : $"Queued {queued} integrity recheck(s).";
            }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { actionState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { actionState.Text = _arabic ? "فشل الطلب." : "Operation failed."; }
        };
        actions.Children.Add(queue);
        actions.Children.Add(recheck);

        var assetId = new TextBox
        {
            MinWidth = 310,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 6, 10, 0),
            ToolTip = _arabic ? "معرّف الأصل GUID" : "Asset GUID"
        };
        var lookup = ActionButton(_arabic ? "عرض حماية الأصل" : "View asset protection");
        var assetState = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = Text(), TextWrapping = TextWrapping.Wrap };
        lookup.Click += async (_, _) =>
        {
            if (!Guid.TryParse(assetId.Text.Trim(), out var id))
            {
                assetState.Text = _arabic ? "أدخل معرّف أصل صحيحًا." : "Enter a valid asset GUID.";
                return;
            }
            try
            {
                assetState.Text = _arabic ? "جاري تحميل حالة الحماية…" : "Loading asset protection…";
                var record = await _protectionClient!.GetAssetAsync(id);
                assetState.Text = record is null
                    ? (_arabic ? "لا يوجد سجل حماية لهذا الأصل." : "No protection record exists for this asset.")
                    : $"{record.State} · Primary {record.PrimaryTargetId} · Backup {record.BackupTargetId}\nSHA-256 {record.ExpectedSha256} · {record.ExpectedLength:N0} bytes" +
                      (string.IsNullOrWhiteSpace(record.LastError) ? string.Empty : $"\n{record.LastError}");
            }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { assetState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { assetState.Text = _arabic ? "تعذر تحميل حالة الحماية." : "Asset protection could not be loaded."; }
        };
        var lookupPanel = new WrapPanel();
        lookupPanel.Children.Add(assetId);
        lookupPanel.Children.Add(lookup);

        var healthCard = health.IsReady
            ? StateCard(_arabic ? "جاهز" : "Ready", health.Detail, "#ECFDF3", "#027A48")
            : StateCard("Degraded", health.Detail, "#FFFAEB", "#B54708");

        var actionPanel = new StackPanel();
        actionPanel.Children.Add(actions);
        actionPanel.Children.Add(actionState);
        var lookupStack = new StackPanel();
        lookupStack.Children.Add(lookupPanel);
        lookupStack.Children.Add(assetState);

        return Scroll(PageStack(
            Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection",
                _arabic ? "Protected لا يتم إعلانها قبل تطابق SHA-256 والحجم." : "Protected is impossible until Backup SHA-256 and length independently match the verified Primary original."),
            MetricRow(
                Metric(summary.Protected.ToString(), "Protected", "Primary + Backup verified"),
                Metric(summary.Pending.ToString(), "Backup Pending", "Awaiting verified copy"),
                Metric(summary.Failed.ToString(), "Backup Failed", "Primary preserved"),
                Metric(summary.Mismatch.ToString(), "Mismatch", "Never silently accepted")),
            healthCard,
            Card(_arabic ? "أهداف التخزين" : "Storage targets", new TextBlock { Text = $"Primary: {health.PrimaryTargetId}\nBackup: {health.BackupTargetId}", Foreground = Text() }),
            Card(_arabic ? "عمليات الحماية" : "Protection actions", actionPanel),
            Card(_arabic ? "حماية أصل محدد" : "Asset protection lookup", lookupStack)));
    }

    private FrameworkElement BuildProtectionUnavailable(string detail) => Scroll(PageStack(
        Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection", detail),
        StateCard("Degraded", detail + (_arabic ? " لن يتم إعلان أي أصل Protected." : " No asset is represented as Protected."), "#FFFAEB", "#B54708")));

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 6, 10, 0),
        Padding = new Thickness(14, 9, 14, 9),
        Background = Gold(),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };
}
