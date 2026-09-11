using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MAM.Desktop;

namespace MAM.P01.VisualAcceptance.Checks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/p01-visual");
        Directory.CreateDirectory(output);

        var app = new Application();
        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            ShowInTaskbar = false
        };

        try
        {
            window.Show();
            var login = (FrameworkElement?)window.FindName("LoginLayer");
            var shell = (FrameworkElement?)window.FindName("ShellLayer");
            if (login is null || shell is null)
                throw new InvalidOperationException("Desktop visual acceptance could not resolve shell layers.");

            login.Visibility = Visibility.Collapsed;
            shell.Visibility = Visibility.Visible;

            Render(window, output, 1366, 768, 96, false, "desktop-1366x768-en.png");
            Render(window, output, 1366, 768, 96, true, "desktop-1366x768-ar.png");
            Render(window, output, 1920, 1080, 96, false, "desktop-1920x1080-en.png");
            Render(window, output, 1920, 1080, 96, true, "desktop-1920x1080-ar.png");
            Render(window, output, 1440, 900, 144, false, "desktop-highdpi-en.png");
            Render(window, output, 1440, 900, 144, true, "desktop-highdpi-ar.png");

            var pngs = Directory.GetFiles(output, "desktop-*.png");
            if (pngs.Length != 6 || pngs.Any(path => new FileInfo(path).Length < 10_000))
                throw new InvalidOperationException("Desktop rendered acceptance did not produce six non-trivial PNG captures.");

            Console.WriteLine("PASS: P01 Windows rendered acceptance generated 1366x768, 1920x1080 and 150% DPI evidence in English LTR and Arabic RTL.");
            return 0;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static void Render(MainWindow window, string output, int width, int height, double dpi, bool arabic, string fileName)
    {
        Invoke(window, "ApplyLanguage", arabic);
        Invoke(window, "ShowPage", "dashboard");

        window.Width = width;
        window.Height = height;
        window.UpdateLayout();

        if (window.Content is not FrameworkElement root)
            throw new InvalidOperationException("Desktop window root is not renderable.");

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var pixelWidth = (int)Math.Ceiling(width * dpi / 96d);
        var pixelHeight = (int)Math.Ceiling(height * dpi / 96d);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(output, fileName);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Invoke(MainWindow window, string methodName, object argument)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Desktop visual acceptance could not resolve {methodName}.");
        method.Invoke(window, new[] { argument });
    }
}
