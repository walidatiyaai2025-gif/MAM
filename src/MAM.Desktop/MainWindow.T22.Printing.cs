using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MAM.Application.Tapes;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly string[] T22Code128Patterns =
    [
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    ];

    private async Task T21PrintBarcodeAsync(TapeInventoryItem tape)
    {
        if (_t21TapeClient is null) return;

        try
        {
            var descriptor = await _t21TapeClient.GetBarcodeAsync(tape.TapeId);
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var visual = BuildT22BarcodeLabel(descriptor, 60, 30);
            PrepareT22PrintVisual(visual, 60, 30);
            printDialog.PrintVisual(visual, $"MAM {tape.TapeCode} barcode");

            await _t21TapeClient.RecordPrintEventAsync(
                tape.TapeId,
                new TapePrintEventRequest("label", "Tape", 60, 30));
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                _arabic ? $"تعذر طباعة الباركود: {ex.Message}" : $"Barcode printing failed: {ex.Message}",
                _arabic ? "طباعة الباركود" : "Barcode printing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task T21PrintReportAsync(TapeInventoryItem tape)
    {
        if (_t21TapeClient is null) return;

        try
        {
            var descriptor = await _t21TapeClient.GetBarcodeAsync(tape.TapeId);
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var visual = BuildT22TapeReport(tape, descriptor);
            PrepareT22PrintVisual(visual, 210, 297);
            printDialog.PrintVisual(visual, $"MAM {tape.TapeCode} report");

            await _t21TapeClient.RecordPrintEventAsync(
                tape.TapeId,
                new TapePrintEventRequest("report", null, 210, 297));
        }
        catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                _arabic ? $"تعذر طباعة التقرير: {ex.Message}" : $"Tape report printing failed: {ex.Message}",
                _arabic ? "تقرير الشريط" : "Tape report",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private FrameworkElement BuildT22BarcodeLabel(TapeBarcodeDescriptor descriptor, double widthMm, double heightMm)
    {
        var root = new Border
        {
            Width = MmToDip(widthMm),
            Height = MmToDip(heightMm),
            Background = Brushes.White,
            Padding = new Thickness(MmToDip(2.5)),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5)
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = descriptor.TapeCode,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(BuildT22Code128Canvas(descriptor.Payload, Math.Max(42, MmToDip(heightMm) * 0.38)));
        stack.Children.Add(new TextBlock
        {
            Text = descriptor.TapeName,
            FontSize = 9,
            Foreground = Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        root.Child = stack;
        return root;
    }

    private FrameworkElement BuildT22TapeReport(TapeInventoryItem tape, TapeBarcodeDescriptor descriptor)
    {
        var root = new Border
        {
            Width = MmToDip(190),
            Background = Brushes.White,
            Padding = new Thickness(MmToDip(8)),
            BorderBrush = Brush("#D0D5DD"),
            BorderThickness = new Thickness(1)
        };
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = _arabic ? "الديوان الأميري" : "Diwan Al Amiri",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Navy(),
            TextAlignment = TextAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = _arabic ? "تقرير رسمي عن الشريط" : "Official Tape Report",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Navy(),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 16)
        });

        stack.Children.Add(BuildT22BarcodeLabel(descriptor, 100, 35));
        stack.Children.Add(T22ReportField(_arabic ? "رقم الشريط" : "Tape number", tape.TapeCode));
        stack.Children.Add(T22ReportField(_arabic ? "اسم الشريط" : "Tape name", tape.Title));
        stack.Children.Add(T22ReportField(_arabic ? "الرقم القديم" : "Legacy number", tape.LegacyNumber));
        stack.Children.Add(T22ReportField(_arabic ? "نوع الشريط" : "Tape format", tape.TapeFormatCode));
        stack.Children.Add(T22ReportField(_arabic ? "الإدارة" : "Department", T22DepartmentName(tape.OwnerDepartment)));
        stack.Children.Add(T22ReportField(_arabic ? "الحالة المادية" : "Physical condition", tape.PhysicalCondition));
        stack.Children.Add(T22ReportField(_arabic ? "حالة الرقمنة" : "Digitization status", tape.DigitizationStatus));
        stack.Children.Add(T22ReportField(_arabic ? "تاريخ التسجيل" : "Recording date", tape.RecordingDate?.ToString("yyyy-MM-dd")));
        stack.Children.Add(T22ReportField(_arabic ? "المدة" : "Duration", tape.DurationSeconds is int seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : null));
        stack.Children.Add(T22ReportField(_arabic ? "الموقع" : "Location", T21Location(tape)));
        stack.Children.Add(T22ReportField(_arabic ? "الوصف" : "Description", tape.Description));
        stack.Children.Add(T22ReportField(_arabic ? "الملاحظات" : "Notes", tape.Notes));
        stack.Children.Add(new TextBlock
        {
            Text = (_arabic ? "تاريخ الطباعة: " : "Printed: ") + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"),
            Foreground = Brush("#667085"),
            FontSize = 10,
            Margin = new Thickness(0, 14, 0, 0)
        });

        root.Child = stack;
        return root;
    }

    private FrameworkElement T22ReportField(string label, string? value)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var key = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = Navy(),
            TextWrapping = TextWrapping.Wrap
        };
        var val = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(key, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(key);
        grid.Children.Add(val);
        return grid;
    }

    private string T22DepartmentName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "—";
        var row = _t21Departments.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        return row is null ? code : (_arabic ? row.NameAr : row.NameEn);
    }

    private static Canvas BuildT22Code128Canvas(string payload, double height)
    {
        var values = T22Code128Values(payload);
        var quiet = 10d;
        var module = 1.5d;
        var x = quiet;
        var bars = new List<(double X, double Width)>();

        foreach (var value in values)
        {
            var pattern = T22Code128Patterns[value];
            var black = true;
            foreach (var ch in pattern)
            {
                var width = ch - '0';
                if (black) bars.Add((x, width));
                x += width;
                black = !black;
            }
        }

        var canvas = new Canvas
        {
            Width = (x + quiet) * module,
            Height = height,
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2)
        };
        foreach (var bar in bars)
        {
            var rect = new Rectangle
            {
                Width = bar.Width * module,
                Height = height,
                Fill = Brushes.Black
            };
            Canvas.SetLeft(rect, bar.X * module);
            Canvas.SetTop(rect, 0);
            canvas.Children.Add(rect);
        }
        return canvas;
    }

    private static IReadOnlyList<int> T22Code128Values(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Barcode payload is required.");

        var data = new List<int>();
        foreach (var ch in payload)
        {
            var code = (int)ch;
            if (code is < 32 or > 126)
                throw new InvalidOperationException("Code 128 B supports ASCII characters 32-126 only.");
            data.Add(code - 32);
        }

        var checksum = 104;
        for (var i = 0; i < data.Count; i++)
            checksum += data[i] * (i + 1);

        var values = new List<int>(data.Count + 3) { 104 };
        values.AddRange(data);
        values.Add(checksum % 103);
        values.Add(106);
        return values;
    }

    private static void PrepareT22PrintVisual(FrameworkElement visual, double widthMm, double heightMm)
    {
        var size = new Size(MmToDip(widthMm), MmToDip(heightMm));
        visual.Measure(size);
        visual.Arrange(new Rect(new Point(0, 0), size));
        visual.UpdateLayout();
    }

    private static double MmToDip(double mm) => mm * 96d / 25.4d;
}
