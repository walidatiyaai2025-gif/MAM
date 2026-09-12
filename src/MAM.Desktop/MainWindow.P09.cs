using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Clients;
using MAM.Application.Operations;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly bool P09LoadedHandlerRegistered = RegisterP09LoadedHandler();
    private MamOperationsApiClient? _p09OperationsClient;
    private bool _p09Wired;

    private static bool RegisterP09LoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P09WindowLoaded));
        return true;
    }

    private static void P09WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP09OperationsUi();
    }

    private void WireP09OperationsUi()
    {
        if (_p09Wired) return;
        _p09Wired = true;
        _titles["reports"] = ("Operations & DR", "التقارير والمراقبة والتعافي");

        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
        {
            var http = new HttpClient
            {
                BaseAddress = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/"),
                Timeout = TimeSpan.FromSeconds(45)
            };
            _p09OperationsClient = new MamOperationsApiClient(http, "WindowsDesktop", Environment.GetEnvironmentVariable("MAM_DEV_USER"));
        }

        var button = new Button
        {
            Tag = "reports",
            Content = "Operations & DR",
            Style = (Style)FindResource("NavButton")
        };
        button.Click += async (_, _) =>
        {
            _currentRoute = "reports";
            PageTitle.Text = _arabic ? _titles["reports"].Ar : _titles["reports"].En;
            await ShowP09OperationsAsync();
        };
        var admin = NavPanel.Children.OfType<Button>().FirstOrDefault(x => string.Equals(x.Tag as string, "admin", StringComparison.OrdinalIgnoreCase));
        var index = admin is null ? NavPanel.Children.Count : NavPanel.Children.IndexOf(admin);
        NavPanel.Children.Insert(index, button);

        LanguageButton.Click += async (_, _) =>
        {
            if (!string.Equals(_currentRoute, "reports", StringComparison.OrdinalIgnoreCase)) return;
            button.Content = _arabic ? _titles["reports"].Ar : _titles["reports"].En;
            await Task.Yield();
            await ShowP09OperationsAsync();
        };
    }

    private async Task ShowP09OperationsAsync()
    {
        if (!string.Equals(_currentRoute, "reports", StringComparison.OrdinalIgnoreCase)) return;
        PageTitle.Text = _arabic ? "التقارير والمراقبة والتعافي" : "Reports, Monitoring, Resilience & DR";
        ContentHost.Content = Scroll(PageStack(
            Lead(PageTitle.Text, _arabic ? "جاري قراءة الحالة التشغيلية المركزية…" : "Loading authoritative operational state from the Central API…"),
            StateCard("Loading", _arabic ? "جاري تحميل P09…" : "Loading P09 operational reports…", "#EFF8FF", "#175CD3")));

        if (_p09OperationsClient is null)
        {
            ContentHost.Content = BuildP09Failure(_arabic ? "واجهة API المركزية غير مهيأة." : "Central API is not configured.", "Degraded");
            return;
        }

        try
        {
            var summaryTask = _p09OperationsClient.GetSummaryAsync();
            var throughputTask = _p09OperationsClient.GetThroughputAsync();
            var queuesTask = _p09OperationsClient.GetQueuesAsync();
            var integrityTask = _p09OperationsClient.GetIntegrityAsync();
            var storageTask = _p09OperationsClient.GetStorageAsync();
            var dependenciesTask = _p09OperationsClient.GetDependenciesAsync();
            await Task.WhenAll(summaryTask, throughputTask, queuesTask, integrityTask, storageTask, dependenciesTask);
            if (!string.Equals(_currentRoute, "reports", StringComparison.OrdinalIgnoreCase)) return;
            ContentHost.Content = BuildP09Dashboard(await summaryTask, await throughputTask, await queuesTask,
                await integrityTask, await storageTask, await dependenciesTask);
        }
        catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            ContentHost.Content = BuildP09Failure(_arabic ? "صلاحية الإدارة مطلوبة لعرض التقارير التشغيلية." : "Administration permission is required for operational reports.", "Permission denied");
        }
        catch
        {
            ContentHost.Content = BuildP09Failure(_arabic ? "تعذر الوصول إلى تقارير P09؛ لا توجد بيانات محلية بديلة." : "P09 operations service is unavailable; no local-authoritative fallback is used.", "API error");
        }
    }

    private FrameworkElement BuildP09Dashboard(OperationalSummary summary, IngestThroughputReport throughput,
        DurableQueueReport queues, IntegrityProtectionReport integrity, StorageUsageReport storage, DependencyHealthReport dependencies)
    {
        var protectionTotal = integrity.Protected + integrity.Pending + integrity.Failed + integrity.Mismatch;
        var coverage = protectionTotal == 0 ? "—" : $"{integrity.Protected * 100d / protectionTotal:F1}%";
        var queueText = queues.Queues.Count == 0
            ? (_arabic ? "لا توجد حالة قوائم." : "No durable queue state.")
            : string.Join(Environment.NewLine, queues.Queues.Select(q =>
                $"{q.Queue} · pending {q.Pending:N0} · leased {q.Leased:N0} · failed {q.Failed:N0} · stale {q.StaleLeases:N0}"));
        var dependencyText = string.Join(Environment.NewLine, dependencies.Items.Select(d => $"{d.Dependency} · {d.Status} · {d.TargetId ?? "—"}"));
        var diagnosticsState = new TextBlock { Foreground = Text(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        var diagnosticsButton = P08ActionButton(_arabic ? "تحميل حزمة التشخيص" : "Load diagnostics bundle");
        diagnosticsButton.Click += async (_, _) =>
        {
            diagnosticsState.Text = _arabic ? "جاري التحميل…" : "Loading secret-safe diagnostics…";
            try
            {
                var bundle = await _p09OperationsClient!.GetDiagnosticsAsync();
                diagnosticsState.Text = $"Correlation: {bundle.CorrelationId}\nVersion: {bundle.Version}\nCommit: {bundle.CommitSha}\n{string.Join(Environment.NewLine, bundle.RedactionPolicy)}";
            }
            catch { diagnosticsState.Text = _arabic ? "تعذر تحميل التشخيص." : "Diagnostics unavailable."; }
        };
        var diagnosticsPanel = new StackPanel();
        diagnosticsPanel.Children.Add(diagnosticsButton);
        diagnosticsPanel.Children.Add(diagnosticsState);

        return Scroll(PageStack(
            Lead(_arabic ? "التقارير والمراقبة والتعافي" : "Reports, Monitoring, Resilience & DR",
                _arabic ? "المؤشرات محسوبة من الحالة الدائمة للخدمات المركزية بدون أهداف إنتاجية مختلقة." : "Metrics are calculated from durable central state without fabricated production targets."),
            MetricRow(
                Metric(summary.Assets.ToString("N0"), _arabic ? "الأصول" : "Assets", $"{summary.Originals:N0} originals"),
                Metric(FormatP09Bytes(summary.OriginalBytes), _arabic ? "حجم الأصول" : "Original bytes", storage.PrimaryTargetId),
                Metric(coverage, _arabic ? "تغطية الحماية" : "Protection coverage", $"{integrity.Protected:N0} verified"),
                Metric(FormatP09Bytes(throughput.CompletedBytes), _arabic ? "إدخال 24 ساعة" : "24h ingest", $"{throughput.CompletedSessions:N0} sessions")),
            Card(_arabic ? "القوائم الدائمة والاسترداد" : "Durable queues & recovery", new TextBlock { Text = queueText, Foreground = Text(), TextWrapping = TextWrapping.Wrap }),
            TwoColumn(
                Card(_arabic ? "سلامة النسخ" : "Integrity & protection", new TextBlock { Text = $"Protected {integrity.Protected:N0}\nPending {integrity.Pending:N0}\nFailed {integrity.Failed:N0}\nMismatch {integrity.Mismatch:N0}\nVerified {FormatP09Bytes(integrity.ProtectedBytes)}", Foreground = Text() }),
                Card(_arabic ? "التخزين الآمن" : "Safe storage view", new TextBlock { Text = $"Primary {storage.PrimaryTargetId} · {FormatP09Bytes(storage.AuthoritativeOriginalBytes)}\nBackup {storage.BackupTargetId} · {FormatP09Bytes(storage.VerifiedProtectedBytes)}\nFilesystem roots and credentials are not exposed.", Foreground = Text(), TextWrapping = TextWrapping.Wrap })),
            Card(_arabic ? "صحة الاعتمادات" : "Dependency health", new TextBlock { Text = dependencyText, Foreground = Text(), TextWrapping = TextWrapping.Wrap }),
            Card(_arabic ? "حزمة التشخيص" : "Diagnostics bundle", diagnosticsPanel)));
    }

    private FrameworkElement BuildP09Failure(string detail, string heading) => Scroll(PageStack(
        Lead(_arabic ? "التقارير والمراقبة والتعافي" : "Reports, Monitoring, Resilience & DR", detail),
        StateCard(heading, detail, heading == "Permission denied" ? "#FFF6ED" : "#FFFAEB", heading == "Permission denied" ? "#C4320A" : "#B54708")));

    private static string FormatP09Bytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, value);
        var index = 0;
        while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return $"{size:F1} {units[index]}";
    }
}
