using Avalonia;

namespace MAM.MacUploader;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MacRuntimeInspector.Install();
        if (args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.Ordinal)))
        {
            Console.WriteLine("DIWAN_MAM_MAC_UPLOADER_SMOKE_OK");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
