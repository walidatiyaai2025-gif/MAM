using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Catalog;
using MAM.Application.Clients;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamCatalogApiClient? _p02CatalogClient;
    private HttpClient? _p02HttpClient;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeP02CatalogIntegration();
    }

    private void InitializeP02CatalogIntegration()
    {
        if (_p02CatalogClient is not null) return;

        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri)) return;

        _p02HttpClient = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(apiUri),
            Timeout = TimeSpan.FromSeconds(20)
        };
        var developmentUser = Environment.GetEnvironmentVariable("MAM_DEV_USER");
        _p02CatalogClient = new MamCatalogApiClient(_p02HttpClient, "WindowsDesktop", developmentUser);

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button => string.Equals(button.Tag as string, "library", StringComparison.OrdinalIgnoreCase)))
            button.Click += P02LibraryNavigate_Click;

        LanguageButton.Click += P02LanguageChanged_Click;
        if (string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase))
            _ = LoadP02LibraryAsync();
    }

    private async void P02LibraryNavigate_Click(object sender, RoutedEventArgs e) => await LoadP02LibraryAsync();

    private async void P02LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase))
            await LoadP02LibraryAsync();
    }

    private async Task LoadP02LibraryAsync()
    {
        if (_p02CatalogClient is null) return;

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "الاتصال بالكتالوج المركزي جارٍ…" : "Connecting to the authoritative Central API catalog…"),
            StateCard("Loading", _arabic ? "جاري تحميل الكتالوج المركزي…" : "Loading authoritative catalog data…", "#EFF8FF", "#175CD3")));

        try
        {
            var assets = await _p02CatalogClient.ListAssetsAsync();
            if (!string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase)) return;

            if (assets.Count == 0)
            {
                ContentHost.Content = Scroll(PageStack(
                    Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "متصل بالكتالوج المركزي." : "Connected to the authoritative Central API catalog."),
                    StateCard("Empty", _arabic ? "لا توجد أصول في الكتالوج المركزي." : "No assets are present in the authoritative catalog.", "#F9FAFB", "#475467")));
                return;
            }

            var rows = new StackPanel();
            foreach (var asset in assets.Take(100))
                rows.Children.Add(ListRow(asset.Id.ToString("D")[..13], asset.Title, $"v{asset.Version} · {asset.Lifecycle}", _arabic ? "مركزي" : "Central"));

            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "بيانات مباشرة من واجهة API المركزية." : "Live data from the authoritative Central API."),
                Toolbar(_arabic ? "بحث ومرشحات · اتصال مركزي" : "Search and filters · Central API"),
                rows));
        }
        catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ShowP02LibraryState("Permission denied", _arabic ? "لا توجد صلاحية لقراءة الكتالوج المركزي." : "The current identity is not authorized to read the Central API catalog.", "#FFF6ED", "#C4320A");
        }
        catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            ShowP02LibraryState("Degraded", _arabic ? "الكتالوج المركزي غير جاهز حاليًا." : "The authoritative catalog dependency is currently degraded.", "#FFFAEB", "#B54708");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or MamApiException)
        {
            ShowP02LibraryState("API error", _arabic ? "تعذر الوصول إلى واجهة API المركزية. يمكن إعادة المحاولة." : "Central API is unreachable. Retry is available.", "#FEF3F2", "#B42318");
        }
    }

    internal Task<AssetSnapshot> CreateP02AssetAsync(string title, CancellationToken cancellationToken = default) =>
        _p02CatalogClient?.CreateAssetAsync(title, cancellationToken)
        ?? Task.FromException<AssetSnapshot>(new InvalidOperationException("Central API integration is not configured."));

    internal Task<AssetSnapshot> UpdateP02AssetTitleAsync(Guid assetId, string title, long expectedVersion, CancellationToken cancellationToken = default) =>
        _p02CatalogClient?.UpdateTitleAsync(assetId, title, expectedVersion, cancellationToken)
        ?? Task.FromException<AssetSnapshot>(new InvalidOperationException("Central API integration is not configured."));

    private void ShowP02LibraryState(string title, string detail, string background, string foreground)
    {
        if (!string.Equals(_currentRoute, "library", StringComparison.OrdinalIgnoreCase)) return;
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "مكتبة الوسائط" : "Media Library", _arabic ? "حالة الاتصال بالكتالوج المركزي." : "Central catalog connection state."),
            StateCard(title, detail, background, foreground)));
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/', StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
