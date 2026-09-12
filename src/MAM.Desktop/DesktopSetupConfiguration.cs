using System.Text.Json;

namespace MAM.Desktop;

internal static class DesktopSetupConfiguration
{
    private sealed class Options
    {
        public string? ApiBaseUrl { get; set; }
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

            ApplyIfUnset("MAM_API_BASE_URL", options.ApiBaseUrl);
            ApplyIfUnset("MAM_CAPTURE_CACHE_ROOT", options.CaptureCacheRoot, expandEnvironmentVariables: true);
            ApplyIfUnset("MAM_CAPTURE_PROVIDER", options.CaptureProvider);
        }
        catch
        {
            // Installer configuration is advisory for client bootstrap. Runtime UI remains fail-closed
            // when the Central API or a certified capture provider is not configured/reachable.
        }
    }

    private static void ApplyIfUnset(string name, string? value, bool expandEnvironmentVariables = false)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) || string.IsNullOrWhiteSpace(value)) return;
        var resolved = expandEnvironmentVariables ? Environment.ExpandEnvironmentVariables(value.Trim()) : value.Trim();
        Environment.SetEnvironmentVariable(name, resolved, EnvironmentVariableTarget.Process);
    }
}
