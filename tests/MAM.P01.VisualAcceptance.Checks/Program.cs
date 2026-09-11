using System;
using System.IO;
using System.Linq;
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

        var app = new System.Windows.Application();
        var window = new MainWindow
        {
            ShowInTaskbar = false
        };

        try
        {
            // Never show the Window on the hosted runner. Showing it lets the interactive
            // runner desktop constrain the client area (for example to ~1028x749), which
            // makes a nominal 1366/1920 capture invalid. The XAML tree is fully initialized
            // by MainWindow's constructor, so we detach and lay out the real RootGrid at the
            // exact logical acceptance size entirely off-screen.
            var login = (FrameworkElement?)window.FindName("LoginLayer");
            var shell = (FrameworkElement?)window.FindName("ShellLayer");
            var root = (FrameworkElement?)window.FindName("RootGrid");
            if (login is null || shell is null || root is null)
                throw new InvalidOperationException("Desktop visual acceptance could not resolve required shell elements.");

            login.Visibility = Visibility.Collapsed;
            shell.Visibility = Visibility.Visible;
            window.Content = null;

            Render(window, root, output, 1366, 768, 96, false, "desktop-1366x768-en.png");
            Render(window, root, output, 1366, 768, 96, true, "desktop-1366x768-ar.png");
            Render(window, root, output, 1920, 1080, 96, false, "desktop-1920x1080-en.png");
            Render(window, root, output, 1920, 1080, 96, true, "desktop-1920x1080-ar.png");
            Render(window, root, output, 1440, 900, 144, false, "desktop-highdpi-en.png");
            Render(window, root, output, 1440, 900, 144, true, "desktop-highdpi-ar.png");

            var pngs = Directory.GetFiles(output, "desktop-*.png");
            if (pngs.Length != 6 || pngs.Any(path => new FileInfo(path).Length < 10_000))
                throw new InvalidOperationException("Desktop rendered acceptance did not produce six non-trivial PNG captures.");

            Console.WriteLine("PASS: P01 Windows rendered acceptance generated exact-size 1366x768, 1920x1080 and 150% DPI evidence in English LTR and Arabic RTL.");
            return 0;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static void Render(MainWindow window, FrameworkElement root, string output, int width, int height, double dpi, bool arabic, string fileName)
    {
        Invoke(window, "ApplyLanguage", arabic);
        Invoke(window, "ShowPage", "dashboard");

        root.Width = width;
        root.Height = height;
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        if (Math.Abs(root.ActualWidth - width) > 0.5 || Math.Abs(root.ActualHeight - height) > 0.5)
            throw new InvalidOperationException($"Desktop off-screen layout size mismatch: requested={width}x{height}, actual={root.ActualWidth}x{root.ActualHeight}.");

        var pixelWidth = (int)Math.Ceiling(width * dpi / 96d);
        var pixelHeight = (int)Math.Ceiling(height * dpi / 96d);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        EnsureRightEdgeRendered(bitmap, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(output, fileName);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void EnsureRightEdgeRendered(RenderTargetBitmap bitmap, string fileName)
    {
        const int sampleWidth = 4;
        var stride = sampleWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth - sampleWidth, 0, sampleWidth, bitmap.PixelHeight), pixels, stride, 0);

        var opaque = 0;
        var total = sampleWidth * bitmap.PixelHeight;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0) opaque++;
        }

        if (opaque < total * 0.90)
            throw new InvalidOperationException($"Desktop rendered evidence has an unrendered right edge: {fileName}; opaque={opaque}/{total}.");
    }

    private static void Invoke(MainWindow window, string methodName, object argument)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Desktop visual acceptance could not resolve {methodName}.");
        method.Invoke(window, new[] { argument });
    }
}
