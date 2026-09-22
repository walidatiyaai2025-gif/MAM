using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace MAM.Desktop;

public partial class MainWindow
{
    private async Task ApplyManagedDesktopNavigationAsync()
    {
        try
        {
            using var client = DesktopProductionTransport.CreateApiClient(TimeSpan.FromSeconds(30));
            if (client is null) return;
            using var response = await client.GetAsync("api/v1/admin/navigation");
            if (!response.IsSuccessStatusCode) return;
            var payload = await response.Content.ReadFromJsonAsync<DesktopNavigationPayload>();
            if (payload?.Items is null) return;

            var byRoute = payload.Items
                .Where(x => x.NavigationKey.StartsWith("desktop-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.NavigationKey["desktop-".Length..], StringComparer.OrdinalIgnoreCase);

            var buttons = NavPanel.Children.OfType<Button>()
                .Where(x => x.Tag is string)
                .ToArray();

            foreach (var button in buttons)
            {
                var route = (string)button.Tag;
                if (!byRoute.TryGetValue(route, out var item)) continue;
                button.Visibility = item.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                button.Content = _arabic ? item.LabelAr : item.LabelEn;
            }

            var ordered = buttons
                .OrderBy(x => byRoute.TryGetValue((string)x.Tag, out var item) ? item.SortOrder : int.MaxValue)
                .ThenBy(x => Array.IndexOf(buttons, x))
                .ToArray();

            foreach (var button in ordered)
            {
                NavPanel.Children.Remove(button);
                NavPanel.Children.Add(button);
            }
        }
        catch
        {
            // Navigation configuration is an enhancement; the safe compiled
            // Desktop menu remains available when the central setting is unreachable.
        }
    }

    private sealed record DesktopNavigationPayload(IReadOnlyList<DesktopNavigationItem> Items);
    private sealed record DesktopNavigationItem(
        string NavigationKey,
        string? RouteKey,
        string? ParentKey,
        string ItemType,
        string LabelEn,
        string LabelAr,
        bool IsEnabled,
        int SortOrder,
        DateTime UpdatedAtUtc,
        string? UpdatedBy);
}
