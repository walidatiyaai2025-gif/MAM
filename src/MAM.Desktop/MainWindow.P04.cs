using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Clients;
using MAM.Application.Processing;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamProcessingApiClient? _p04ProcessingClient;
    private HttpClient? _p04HttpClient;

    private void InitializeP04ProcessingIntegration()
    {
        if (_p04ProcessingClient is not null) return;
        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri)) return;
        _p04HttpClient = new HttpClient { BaseAddress = EnsureTrailingSlash(apiUri), Timeout = TimeSpan.FromMinutes(5) };
        _p04ProcessingClient = new MamProcessingApiClient(_p04HttpClient, "WindowsDesktop", Environment.GetEnvironmentVariable("MAM_DEV_USER"));

        foreach (var button in NavPanel.Children.OfType<Button>())
        {
            if (string.Equals(button.Tag as string, "asset", StringComparison.OrdinalIgnoreCase)) button.Click += P04AssetNavigate_Click;
            if (string.Equals(button.Tag as string, "queue", StringComparison.OrdinalIgnoreCase)) button.Click += P04QueueNavigate_Click;
        }
        LanguageButton.Click += P04LanguageChanged_Click;
        if (string.Equals(_currentRoute, "asset", StringComparison.OrdinalIgnoreCase)) _ = LoadP04AssetDetailsAsync();
        if (string.Equals(_currentRoute, "queue", StringComparison.OrdinalIgnoreCase)) _ = LoadP04QueueAsync();
    }

    private async void P04AssetNavigate_Click(object sender, RoutedEventArgs e) => await LoadP04AssetDetailsAsync();
    private async void P04QueueNavigate_Click(object sender, RoutedEventArgs e) => await LoadP04QueueAsync();
    private async void P04LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentRoute, "asset", StringComparison.OrdinalIgnoreCase)) await LoadP04AssetDetailsAsync();
        if (string.Equals(_currentRoute, "queue", StringComparison.OrdinalIgnoreCase)) await LoadP04QueueAsync();
    }

    private async Task LoadP04AssetDetailsAsync()
    {
        if (_p04ProcessingClient is null || _p02CatalogClient is null) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "تفاصيل الأصل" : "Asset Details", _arabic ? "جاري تحميل البيانات الفنية الموثقة…" : "Loading authoritative technical metadata…"),
            StateCard("Loading", _arabic ? "جاري الاتصال بخدمة المعالجة المركزية…" : "Connecting to the Central API processing service…", "#EFF8FF", "#175CD3")));
        try
        {
            var assets = await _p02CatalogClient.ListAssetsAsync();
            if (!string.Equals(_currentRoute, "asset", StringComparison.OrdinalIgnoreCase)) return;
            if (assets.Count == 0)
            {
                ShowP04State("asset", "Empty", _arabic ? "لا توجد أصول موثقة للعرض." : "No catalog assets are available for technical inspection.", "#F9FAFB", "#475467");
                return;
            }
            var asset = assets[0];
            var technical = await _p04ProcessingClient.GetTechnicalAsync(asset.Id);
            var derivatives = await _p04ProcessingClient.ListDerivativesAsync(asset.Id);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var inspect = P04ActionButton(_arabic ? "فحص فني" : "Queue inspection");
            inspect.Click += async (_, _) => { await P04EnqueueAsync(asset.Id, BuiltInProcessingProfiles.Inspect); await LoadP04AssetDetailsAsync(); };
            actions.Children.Add(inspect);
            if (technical?.MediaType == "Video")
            {
                var proxy = P04ActionButton(_arabic ? "إنشاء Proxy" : "Queue video proxy");
                proxy.Click += async (_, _) => { await P04EnqueueAsync(asset.Id, BuiltInProcessingProfiles.VideoProxy); await LoadP04AssetDetailsAsync(); };
                actions.Children.Add(proxy);
            }
            if (technical?.MediaType == "Image")
            {
                var preview = P04ActionButton(_arabic ? "إنشاء معاينة" : "Queue image preview");
                preview.Click += async (_, _) => { await P04EnqueueAsync(asset.Id, BuiltInProcessingProfiles.ImagePreview); await LoadP04AssetDetailsAsync(); };
                actions.Children.Add(preview);
            }
            if (technical?.MediaType == "Audio")
            {
                var audio = P04ActionButton(_arabic ? "إنشاء معاينة صوت" : "Queue audio preview");
                audio.Click += async (_, _) => { await P04EnqueueAsync(asset.Id, BuiltInProcessingProfiles.AudioPreview); await LoadP04AssetDetailsAsync(); };
                actions.Children.Add(audio);
            }

            var technicalText = technical is null
                ? (_arabic ? "لم يتم الفحص بعد. استخدم فحص فني ثم راجع قائمة المعالجة." : "Not inspected yet. Queue technical inspection, then review Processing Queue.")
                : $"{technical.MediaType} · {technical.VideoCodec ?? "—"} · {technical.AudioCodec ?? "—"}\n{technical.Width?.ToString() ?? "—"}×{technical.Height?.ToString() ?? "—"} · {technical.DurationSeconds?.ToString("0.###") ?? "—"}s\n{(_arabic ? "تم الفحص" : "Inspected")}: {technical.InspectedAtUtc:O}";
            var derivativeText = derivatives.Count == 0
                ? (_arabic ? "لا توجد مشتقات بعد." : "No verified derivatives yet.")
                : string.Join("\n", derivatives.Select(d => $"{d.ProfileId} v{d.ProfileVersion} · {d.ContentType} · {d.Length:N0} B · SHA {d.Sha256[..12]}…"));

            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "تفاصيل الأصل" : "Asset Details", $"{asset.Id:D} · {asset.Title}"),
                Card(_arabic ? "إجراءات المعالجة" : "Processing actions", actions),
                TwoColumn(
                    Card(_arabic ? "البيانات الفنية" : "Technical metadata", new TextBlock { Text = technicalText, TextWrapping = TextWrapping.Wrap, Foreground = Text() }),
                    Card(_arabic ? "المعاينات والمشتقات" : "Verified previews & derivatives", new TextBlock { Text = derivativeText, TextWrapping = TextWrapping.Wrap, Foreground = Text() })),
                StateCard("Central API", _arabic ? "الوصول للميديا والمعالجة يتم عبر الخادم فقط؛ لا توجد بيانات تخزين على العميل." : "Media and processing access is server-mediated only; no storage credentials are present on the client.", "#ECFDF3", "#027A48")));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP04State("asset", "Permission denied", _arabic ? "لا توجد صلاحية لعرض البيانات الفنية." : "Permission denied for technical metadata.", "#FFF6ED", "#C4320A");
        }
        catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            ShowP04State("asset", "Degraded", _arabic ? "خدمة المعالجة غير جاهزة حاليًا." : "The processing dependency is degraded.", "#FFFAEB", "#B54708");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowP04State("asset", "API error", _arabic ? "تعذر تحميل تفاصيل المعالجة. يمكن إعادة المحاولة." : "Processing details could not be loaded. Retry is available.", "#FEF3F2", "#B42318");
        }
    }

    private async Task LoadP04QueueAsync()
    {
        if (_p04ProcessingClient is null) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "قائمة المعالجة" : "Processing Queue", _arabic ? "حالة مباشرة من مخزن الوظائف المركزي." : "Live authoritative state from the durable processing job store."),
            StateCard("Loading", _arabic ? "جاري تحميل الوظائف…" : "Loading durable processing jobs…", "#EFF8FF", "#175CD3")));
        try
        {
            var jobs = await _p04ProcessingClient.ListJobsAsync(100);
            if (!string.Equals(_currentRoute, "queue", StringComparison.OrdinalIgnoreCase)) return;
            if (jobs.Count == 0)
            {
                ShowP04State("queue", "Empty", _arabic ? "لا توجد وظائف معالجة." : "No processing jobs are queued.", "#F9FAFB", "#475467");
                return;
            }
            var stack = new StackPanel();
            foreach (var job in jobs)
            {
                var state = job.State switch
                {
                    ProcessingJobState.Queued => _arabic ? "في الانتظار" : "Queued",
                    ProcessingJobState.Leased => _arabic ? "قيد المعالجة" : "Running",
                    ProcessingJobState.Succeeded => _arabic ? "مكتمل" : "Completed",
                    _ => _arabic ? "فشل · إعادة المحاولة متاحة" : "Failed · Retry available"
                };
                var row = new StackPanel();
                row.Children.Add(new TextBlock { Text = $"{job.JobId:D} · {job.ProfileId} v{job.ProfileVersion}", FontWeight = FontWeights.SemiBold, Foreground = Navy(), TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = $"{state} · attempt {job.AttemptCount} · asset {job.AssetId:D}" + (job.LastError is null ? string.Empty : $"\n{job.LastError}"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Text() });
                if (job.State == ProcessingJobState.Failed)
                {
                    var retry = P04ActionButton(_arabic ? "إعادة المحاولة" : "Retry");
                    retry.Click += async (_, _) => { try { await _p04ProcessingClient.RetryAsync(job.JobId); } catch { } await LoadP04QueueAsync(); };
                    row.Children.Add(retry);
                }
                stack.Children.Add(Card(string.Empty, row));
            }
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "قائمة المعالجة" : "Processing Queue", _arabic ? "حالة مباشرة من SQL عبر Central API." : "Live durable SQL job state through the Central API."), stack));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP04State("queue", "Permission denied", _arabic ? "لا توجد صلاحية لقائمة المعالجة." : "Permission denied for the processing queue.", "#FFF6ED", "#C4320A");
        }
        catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            ShowP04State("queue", "Degraded", _arabic ? "مخزن الوظائف أو خدمة المعالجة غير جاهزة." : "The job store or processing service is degraded.", "#FFFAEB", "#B54708");
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException)
        {
            ShowP04State("queue", "API error", _arabic ? "تعذر تحميل قائمة المعالجة." : "Processing queue could not be loaded.", "#FEF3F2", "#B42318");
        }
    }

    private async Task P04EnqueueAsync(Guid assetId, string profileId)
    {
        if (_p04ProcessingClient is null) return;
        try { await _p04ProcessingClient.EnqueueAsync(assetId, profileId); }
        catch { }
    }

    private static Button P04ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 8, 10, 0),
        Padding = new Thickness(14, 9, 14, 9),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = Gold(),
        Foreground = System.Windows.Media.Brushes.White,
        BorderThickness = new Thickness(0)
    };

    private void ShowP04State(string route, string title, string detail, string background, string foreground)
    {
        if (!string.Equals(_currentRoute, route, StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(route == "queue" ? (_arabic ? "قائمة المعالجة" : "Processing Queue") : (_arabic ? "تفاصيل الأصل" : "Asset Details"),
                _arabic ? "حالة خدمة المعالجة المركزية." : "Central processing service state."),
            StateCard(title, detail, background, foreground)));
    }
}
