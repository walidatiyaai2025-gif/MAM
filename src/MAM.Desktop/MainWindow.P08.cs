using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Administration;
using MAM.Application.Clients;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly bool P08LoadedHandlerRegistered = RegisterP08LoadedHandler();
    private MamAdministrationApiClient? _p08AdministrationClient;
    private bool _p08Wired;

    private static bool RegisterP08LoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P08WindowLoaded));
        return true;
    }

    private static void P08WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP08AdministrationUi();
    }

    private void WireP08AdministrationUi()
    {
        if (_p08Wired) return;
        _p08Wired = true;
        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
        {
            var http = new HttpClient
            {
                BaseAddress = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/"),
                Timeout = TimeSpan.FromSeconds(45)
            };
            _p08AdministrationClient = new MamAdministrationApiClient(http, "WindowsDesktop", Environment.GetEnvironmentVariable("MAM_DEV_USER"));
        }

        foreach (var button in NavPanel.Children.OfType<Button>().Where(x => string.Equals(x.Tag as string, "admin", StringComparison.OrdinalIgnoreCase)))
        {
            button.Click += async (_, _) =>
            {
                await Task.Yield();
                await ShowP08AdministrationAsync();
            };
        }
        LanguageButton.Click += async (_, _) =>
        {
            if (!string.Equals(_currentRoute, "admin", StringComparison.OrdinalIgnoreCase)) return;
            await Task.Yield();
            await ShowP08AdministrationAsync();
        };
    }

    private async Task ShowP08AdministrationAsync()
    {
        if (!string.Equals(_currentRoute, "admin", StringComparison.OrdinalIgnoreCase)) return;
        PageTitle.Text = _arabic ? "إدارة المؤسسة والسياسات" : "Enterprise Administration & Policy";
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "إدارة المؤسسة والسياسات" : "Enterprise Administration & Policy",
                _arabic ? "جاري تحميل السياسات والمستخدمين والتدقيق من الخدمة المركزية…" : "Loading authoritative policies, users and audit state from the Central API…"),
            StateCard("Loading", _arabic ? "جاري تحميل P08…" : "Loading P08 administration state…", "#EFF8FF", "#175CD3")));

        if (_p08AdministrationClient is null)
        {
            ContentHost.Content = BuildP08Failure(_arabic ? "واجهة API المركزية غير مهيأة." : "Central API is not configured.", "Degraded");
            return;
        }

        try
        {
            var overviewTask = _p08AdministrationClient.GetOverviewAsync();
            var policiesTask = _p08AdministrationClient.ListPoliciesAsync();
            var usersTask = _p08AdministrationClient.ListUsersAsync();
            var auditTask = _p08AdministrationClient.QueryAuditAsync(new AdminAuditQuery(Limit: 10));
            var healthTask = _p08AdministrationClient.GetHealthAsync();
            await Task.WhenAll(overviewTask, policiesTask, usersTask, auditTask, healthTask);
            if (!string.Equals(_currentRoute, "admin", StringComparison.OrdinalIgnoreCase)) return;
            ContentHost.Content = BuildP08Dashboard(await overviewTask, await policiesTask, await usersTask, await auditTask, await healthTask);
        }
        catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            ContentHost.Content = BuildP08Failure(_arabic ? "صلاحية Administrator مطلوبة لإدارة المؤسسة." : "Administrator permission is required for enterprise administration.", "Permission denied");
        }
        catch (Exception)
        {
            ContentHost.Content = BuildP08Failure(_arabic ? "تعذر الوصول إلى خدمة إدارة P08؛ لا يتم تطبيق أي تغيير محلي." : "P08 administration service is unavailable; no local-authoritative fallback is used.", "API error");
        }
    }

    private FrameworkElement BuildP08Dashboard(
        AdministrationOverview overview,
        IReadOnlyList<AdminPolicyRecord> policies,
        IReadOnlyList<AdminUserPolicyRecord> users,
        AdminAuditResult audit,
        AdministrationHealth health)
    {
        var policyStack = new StackPanel();
        foreach (var policy in policies.Take(12))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = $"{policy.DisplayNameEn} · {policy.Category} · v{policy.Version} · {(policy.RequiresRestart ? "Restart required" : "Live")}\nSecretRef: {policy.SecretRef ?? "—"}",
                Foreground = Text(), TextWrapping = TextWrapping.Wrap
            });
            policyStack.Children.Add(row);
        }
        if (policies.Count == 0) policyStack.Children.Add(new TextBlock { Text = _arabic ? "لا توجد سياسات." : "No policies.", Foreground = Text() });

        var testState = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Text(), TextWrapping = TextWrapping.Wrap };
        var testButton = P08ActionButton(_arabic ? "اختبار مرجع التخزين" : "Test storage reference");
        testButton.Click += async (_, _) =>
        {
            testState.Text = _arabic ? "جاري الاختبار بدون إظهار السر…" : "Testing server-side reference without exposing secret material…";
            try
            {
                var target = policies.FirstOrDefault(x => x.PolicyKey == "storage.primary") ?? policies.FirstOrDefault();
                if (target is null) { testState.Text = _arabic ? "لا توجد سياسة للاختبار." : "No policy is available to test."; return; }
                var result = await _p08AdministrationClient!.TestPolicyAsync(target.PolicyKey);
                testState.Text = $"{result.Code}: {result.Detail}";
            }
            catch (MamApiException ex) when ((int)ex.StatusCode == 409) { testState.Text = _arabic ? "Conflict: أعد تحميل السياسة قبل المتابعة." : "Conflict: refresh the policy before continuing."; }
            catch (MamApiException ex) when ((int)ex.StatusCode is 401 or 403) { testState.Text = _arabic ? "لا توجد صلاحية." : "Permission denied."; }
            catch { testState.Text = _arabic ? "فشل الاختبار." : "Reference test failed."; }
        };

        var userText = users.Count == 0
            ? (_arabic ? "لا توجد سجلات مستخدمين محلية بعد؛ توفير هوية الإنتاج مؤجل P12." : "No local user-policy records yet; production identity provisioning remains P12 owner-last.")
            : string.Join(Environment.NewLine, users.Take(8).Select(u => $"{u.DisplayName} · {u.UserName} · {string.Join(", ", u.Roles)} · v{u.Version}"));
        var auditText = audit.Items.Count == 0
            ? (_arabic ? "لا توجد أحداث تدقيق." : "No audit events.")
            : string.Join(Environment.NewLine, audit.Items.Take(10).Select(a => $"{a.OccurredAtUtc:yyyy-MM-dd HH:mm:ss}Z · {a.ActorId} · {a.Action} · {a.Outcome}"));

        var healthCard = health.IsReady
            ? StateCard(_arabic ? "جاهز" : "Ready", health.Detail, "#ECFDF3", "#027A48")
            : StateCard("Degraded", health.Detail, "#FFFAEB", "#B54708");

        var testPanel = new StackPanel();
        testPanel.Children.Add(testButton);
        testPanel.Children.Add(testState);

        return Scroll(PageStack(
            Lead(_arabic ? "إدارة المؤسسة والسياسات" : "Enterprise Administration & Policy",
                _arabic ? "الإدارة مركزية ومحمية بالصلاحيات والإصدار والتدقيق؛ لا يتم عرض القيم السرية." : "Administration is central, permission-protected, versioned and audited; resolved secret values are never displayed."),
            MetricRow(
                Metric(overview.Policies.ToString(), _arabic ? "السياسات" : "Policies", $"{overview.EnabledPolicies} enabled"),
                Metric(overview.Users.ToString(), _arabic ? "المستخدمون" : "Users", "Central authorization policy"),
                Metric(overview.DictionaryEntries.ToString(), _arabic ? "القواميس" : "Dictionaries", "Arabic + English"),
                Metric(overview.RestartRequired.ToString(), _arabic ? "إعادة تشغيل" : "Restart impact", "Explicit operational impact")),
            healthCard,
            Card(_arabic ? "السياسات المركزية" : "Authoritative policies", policyStack),
            Card(_arabic ? "اختبار آمن" : "Safe validation / test", testPanel),
            Card(_arabic ? "المستخدمون والأدوار" : "Users & roles", new TextBlock { Text = userText, Foreground = Text(), TextWrapping = TextWrapping.Wrap }),
            Card(_arabic ? "سجل التدقيق" : "Audit explorer", new TextBlock { Text = auditText, Foreground = Text(), TextWrapping = TextWrapping.Wrap })));
    }

    private FrameworkElement BuildP08Failure(string detail, string heading) => Scroll(PageStack(
        Lead(_arabic ? "إدارة المؤسسة والسياسات" : "Enterprise Administration & Policy", detail),
        StateCard(heading, detail, heading == "Permission denied" ? "#FFF6ED" : "#FFFAEB", heading == "Permission denied" ? "#C4320A" : "#B54708")));

    private static Button P08ActionButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(14, 9, 14, 9),
        Margin = new Thickness(0, 4, 10, 0),
        Background = Gold(),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };
}
