using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MAM.Desktop;

namespace MAM.P01.VisualAcceptance.Checks;

internal static class Program
{
    private static readonly string[] ArabicForbiddenChrome =
    [
        "Loading", "Empty", "API error", "Permission denied", "Degraded / Retry",
        "Demo assets", "Demo protected", "Primary + Backup verified", "DEMO QUEUE",
        "Video preview shell", "Title · date · category · tags · preservation notes",
        "Windows-only capture workspace", "Temporary local selection before central upload",
        "Drop files here or browse", "Proxy generation", "Thumbnail generation",
        "This action requires the System Administrator role", "Secrets are never displayed"
    ];

    private static readonly string[] ArabicForbiddenCaptureRuntime =
    [
        "BLOCK:", "WARN:", "Capture device is unavailable", "Selected capture profile is unsupported",
        "Temporary ingest cache is unavailable", "Required cache capacity must be positive",
        "insufficient free capacity", "offline recovery policy is not explicitly approved",
        "Tape ID, device profile, container and codec", "TIMECODE ", "Dropped frames:",
        "Capture start failed:", "Status refresh failed:", "Resume failed:",
        "Handoff stopped without deleting the temporary capture:"
    ];

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

            AuditAllBaseRoutesInArabic(window, root);

            var pngs = Directory.GetFiles(output, "desktop-*.png");
            if (pngs.Length != 6 || pngs.Any(path => new FileInfo(path).Length < 10_000))
                throw new InvalidOperationException("Desktop rendered acceptance did not produce six non-trivial PNG captures.");

            Console.WriteLine("PASS: P01 Windows rendered acceptance generated exact-size 1366x768, 1920x1080 and 150% DPI evidence in English LTR and Arabic RTL.");
            Console.WriteLine("PASS: Arabic Desktop base-route audit found no known untranslated repository-controlled UI chrome and exercised dynamic Tape Capture localization.");
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
        if (arabic) InvokeNoArgs(window, "ApplyVisibleArabicLocalization");

        root.Width = width;
        root.Height = height;
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        if (arabic) InvokeNoArgs(window, "ApplyVisibleArabicLocalization");

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

    private static void AuditAllBaseRoutesInArabic(MainWindow window, FrameworkElement root)
    {
        Invoke(window, "ApplyLanguage", true);
        foreach (var route in new[] { "dashboard", "library", "asset", "ingest", "capture", "upload", "queue", "admin", "settings" })
        {
            Invoke(window, "ShowPage", route);
            InvokeNoArgs(window, "ApplyVisibleArabicLocalization");
            if (route == "capture") InvokeNoArgs(window, "ApplyP07RuntimeArabicLocalization");
            root.Measure(new Size(1440, 900));
            root.Arrange(new Rect(0, 0, 1440, 900));
            root.UpdateLayout();
            InvokeNoArgs(window, "ApplyVisibleArabicLocalization");
            if (route == "capture") InvokeNoArgs(window, "ApplyP07RuntimeArabicLocalization");

            var visibleText = string.Join("\n", Walk(root).OfType<TextBlock>()
                .Where(x => x.Visibility == Visibility.Visible)
                .Select(x => x.Text)
                .Concat(Walk(root).OfType<Button>()
                    .Where(x => x.Visibility == Visibility.Visible && x.Content is string)
                    .Select(x => (string)x.Content)));

            if (visibleText.Length < 80)
                throw new InvalidOperationException($"Arabic Desktop localization audit collected suspiciously little visible text on route '{route}' ({visibleText.Length} chars).");

            foreach (var forbidden in ArabicForbiddenChrome)
                if (visibleText.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Arabic Desktop localization audit failed on route '{route}': untranslated UI chrome '{forbidden}'.");

            if (route == "capture")
                foreach (var forbidden in ArabicForbiddenCaptureRuntime)
                    if (visibleText.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Arabic Desktop Tape Capture localization audit failed: untranslated runtime chrome '{forbidden}'.");
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var child in LogicalTreeHelper.GetChildren(current))
                if (child is DependencyObject dependencyObject) stack.Push(dependencyObject);
            try
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) stack.Push(VisualTreeHelper.GetChild(current, i));
            }
            catch (InvalidOperationException) { }
        }
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

    private static void InvokeNoArgs(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Desktop visual acceptance could not resolve {methodName}.");
        method.Invoke(window, null);
    }
}
