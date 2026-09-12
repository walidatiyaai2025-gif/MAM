using System;
using System.Linq;
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

        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (button.Tag is not string route || route is not ("admin" or "asset")) continue;
            button.Click += async (_, _) =>
            {
                if (route == "admin") await ShowProtectionAdministrationAsync();
                else await ShowProtectionAssetAsync();
            };
        }
    }

    private async Task ShowProtectionAdministrationAsync()
    {
        if (_currentRoute != "admin") return;
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
            if (_currentRoute != "admin") return;
            var summary = await summaryTask;
            var health = await healthTask;
            ContentHost.Content = BuildProtectionAdmin(summary, health);
        }
        catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection", _arabic ? "الصلاحية مطلوبة." : "Authorization is required."),
                StateCard("Permission denied", _arabic ? "لا توجد صلاحية لإدارة الحماية." : "The current identity cannot administer protection.", "#FFF6ED", "#C4320A")));
        }
        catch (Exception)
        {
            ContentHost.Content = BuildProtectionUnavailable(_arabic ? "تعذر الوصول إلى خدمة الحماية." : "Protection service is unavailable.");
        }
    }

    private FrameworkElement BuildProtectionAdmin(BackupProtectionSummary summary, BackupProtectionHealth health)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var queue = ActionButton(_arabic ? "إضافة النسخ المعلقة" : "Queue pending copies");
        var recheck = ActionButton(_arabic ? "إعادة فحص السلامة" : "Queue integrity recheck");
        var actionState = new TextBlock { Margin = new Thickness(14, 8, 0, 0), Foreground = Text(), TextWrapping = TextWrapping.Wrap };
        queue.Click += async (_, _) =>
        {
            try { actionState.Text = $"Queued: {await _protectionClient!.QueueAsync()}"; await ShowProtectionAdministrationAsync(); }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { actionState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { actionState.Text = _arabic ? "فشل الطلب." : "Operation failed."; }
        };
        recheck.Click += async (_, _) =>
        {
            try { actionState.Text = $"Integrity rechecks queued: {await _protectionClient!.QueueIntegrityRecheckAsync(24)}"; }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { actionState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { actionState.Text = _arabic ? "فشل الطلب." : "Operation failed."; }
        };
        actions.Children.Add(queue);
        actions.Children.Add(recheck);
        actions.Children.Add(actionState);

        var healthCard = health.IsReady
            ? StateCard(_arabic ? "جاهز" : "Ready", health.Detail, "#ECFDF3", "#027A48")
            : StateCard("Degraded", health.Detail, "#FFFAEB", "#B54708");

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
            Card(_arabic ? "عمليات المسؤول" : "Administrator actions", actions)));
    }

    private async Task ShowProtectionAssetAsync()
    {
        if (_currentRoute != "asset" || _protectionClient is null) return;
        var raw = Environment.GetEnvironmentVariable("MAM_SELECTED_ASSET_ID");
        if (!Guid.TryParse(raw, out var assetId)) return;
        try
        {
            var record = await _protectionClient.GetAssetAsync(assetId);
            if (_currentRoute != "asset" || record is null) return;
            var stateColor = record.State == BackupProtectionState.Protected ? "#027A48" : record.State == BackupProtectionState.Mismatch ? "#B42318" : "#B54708";
            var stateBackground = record.State == BackupProtectionState.Protected ? "#ECFDF3" : record.State == BackupProtectionState.Mismatch ? "#FEF3F2" : "#FFFAEB";
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "تفاصيل الأصل" : "Asset Details", assetId.ToString("D")),
                StateCard(record.State.ToString(), record.LastError ?? (_arabic ? "الحالة موثقة من الخدمة المركزية." : "Authoritative protection state from the Central API."), stateBackground, stateColor),
                Card(_arabic ? "التحقق" : "Verification", new TextBlock { Text = $"Primary: {record.PrimaryTargetId}\nBackup: {record.BackupTargetId}\nSHA-256: {record.ExpectedSha256}\nLength: {record.ExpectedLength:N0}", Foreground = Text(), TextWrapping = TextWrapping.Wrap })));
        }
        catch { }
    }

    private FrameworkElement BuildProtectionUnavailable(string detail) => Scroll(PageStack(
        Lead(_arabic ? "حماية النسخة الاحتياطية" : "Backup Protection", detail),
        StateCard("Degraded", detail + (_arabic ? " لن يتم إعلان أي أصل Protected." : " No asset is represented as Protected."), "#FFFAEB", "#B54708")));

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 0, 10, 0),
        Padding = new Thickness(14, 9, 14, 9),
        Background = Gold(),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };
}
