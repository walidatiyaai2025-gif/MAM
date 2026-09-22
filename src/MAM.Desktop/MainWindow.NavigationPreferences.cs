using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MAM.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, (string En, string Ar)> _managedNavigationTitles = new(StringComparer.OrdinalIgnoreCase);

    private (string En, string Ar) NavigationTitle(string route) =>
        _managedNavigationTitles.TryGetValue(route, out var managed) ? managed :
        _titles.TryGetValue(route, out var builtIn) ? builtIn :
        (route, route);

    private async Task LoadDesktopNavigationAsync()
    {
        try
        {
            using var client = DesktopProductionTransport.CreateApiClient(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync("api/v1/admin/navigation");
            if (!response.IsSuccessStatusCode) return;

            var payload = await response.Content.ReadFromJsonAsync<DesktopNavigationEnvelope>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
            var items = payload?.Items?
                .Where(x => x.NavigationKey.StartsWith("desktop-", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<DesktopNavigationItem>();
            if (items.Length == 0) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var buttons = NavPanel.Children.OfType<Button>().ToArray();
                var configByRoute = items
                    .Where(x => !string.IsNullOrWhiteSpace(x.RouteKey))
                    .GroupBy(x => x.RouteKey!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.OrderBy(v => v.SortOrder).First(), StringComparer.OrdinalIgnoreCase);

                _managedNavigationTitles.Clear();
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.RouteKey)) continue;
                    _managedNavigationTitles[item.RouteKey] = (item.LabelEn, item.LabelAr);
                }

                foreach (var button in buttons)
                {
                    if (button.Tag is not string route) continue;
                    button.Visibility = configByRoute.TryGetValue(route, out var config) && !config.IsEnabled
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                var ordered = buttons
                    .Select((button, index) => new
                    {
                        Button = button,
                        Index = index,
                        Order = button.Tag is string route && configByRoute.TryGetValue(route, out var config)
                            ? config.SortOrder
                            : 5000 + index
                    })
                    .OrderBy(x => x.Order)
                    .ThenBy(x => x.Index)
                    .Select(x => x.Button)
                    .ToArray();

                foreach (var button in buttons) NavPanel.Children.Remove(button);
                foreach (var button in ordered) NavPanel.Children.Add(button);

                ApplyLanguage(_arabic);
                if (NavPanel.Children.OfType<Button>().FirstOrDefault(b =>
                        b.Tag is string route &&
                        route.Equals(_currentRoute, StringComparison.OrdinalIgnoreCase) &&
                        b.Visibility == Visibility.Visible) is null)
                {
                    var first = NavPanel.Children.OfType<Button>().FirstOrDefault(b => b.Visibility == Visibility.Visible);
                    if (first?.Tag is string fallback) ShowPage(fallback);
                }
            });
        }
        catch
        {
            // Central navigation is an enhancement. The compiled menu remains a safe fallback.
        }
    }

    private sealed record DesktopNavigationEnvelope(DesktopNavigationItem[] Items);
    private sealed record DesktopNavigationItem(
        string NavigationKey,
        string? RouteKey,
        string? ParentKey,
        string ItemType,
        string LabelEn,
        string LabelAr,
        bool IsEnabled,
        int SortOrder);
}
