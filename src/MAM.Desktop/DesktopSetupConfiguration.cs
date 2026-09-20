using System.Text.Json;

namespace MAM.Desktop;

internal static class DesktopSetupConfiguration
{
    private sealed class Options
    {
        public string? EnvironmentName { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? WebBaseUrl { get; set; }
        public string? CaptureCacheRoot { get; set; }
        public string? CaptureProvider { get; set; }
    }

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Diwan Al Amiri", "MAM", "desktop.setup.json");

    public static void Apply()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var options = JsonSerializer.Deserialize<Options>(
                File.ReadAllText(ConfigPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (options is null) return;

            var environmentName = string.IsNullOrWhiteSpace(options.EnvironmentName)
                ? "Production"
                : options.EnvironmentName.Trim();

            if (string.Equals(environmentName, DesktopProductionTransport.ProductionEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                var productionOrigin = DesktopProductionTransport.ProductionOrigin;
                ValidateProductionUrl(options.ApiBaseUrl, "apiBaseUrl");
                ValidateProductionUrl(options.WebBaseUrl, "webBaseUrl");

                Environment.SetEnvironmentVariable("MAM_DESKTOP_ENVIRONMENT", DesktopProductionTransport.ProductionEnvironment, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", DesktopProductionTransport.ProductionEnvironment, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("MAM_API_BASE_URL", productionOrigin, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("MAM_WEB_BASE_URL", productionOrigin, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("MAM_DEV_USER", null, EnvironmentVariableTarget.Process);
            }
            else
            {
                Environment.SetEnvironmentVariable("MAM_DESKTOP_ENVIRONMENT", environmentName, EnvironmentVariableTarget.Process);
                ApplyIfUnset("MAM_API_BASE_URL", options.ApiBaseUrl);
                ApplyIfUnset("MAM_WEB_BASE_URL", options.WebBaseUrl);
            }

            ApplyIfUnset("MAM_CAPTURE_CACHE_ROOT", options.CaptureCacheRoot, expandEnvironmentVariables: true);
            ApplyIfUnset("MAM_CAPTURE_PROVIDER", options.CaptureProvider);
        }
        catch
        {
            // Installer configuration is advisory for client bootstrap. Runtime UI remains fail-closed
            // when the Central API or a certified capture provider is not configured/reachable.
        }
    }

    private static void ValidateProductionUrl(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("mam.da.gov.kw", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort)
        {
            throw new InvalidOperationException($"{name} must be {DesktopProductionTransport.ProductionOrigin} for Production Desktop.");
        }
    }

    private static void ApplyIfUnset(string name, string? value, bool expandEnvironmentVariables = false)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) || string.IsNullOrWhiteSpace(value)) return;
        var resolved = expandEnvironmentVariables ? Environment.ExpandEnvironmentVariables(value.Trim()) : value.Trim();
        Environment.SetEnvironmentVariable(name, resolved, EnvironmentVariableTarget.Process);
    }
}
