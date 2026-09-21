using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MAM.Application.Branding;

namespace MAM.MacUploader;

public sealed class MainWindow : Window
{
    private readonly MacUploaderSession _session = new();
    private readonly List<string> _selectedFiles = new();

    private readonly StackPanel _loginPanel = new();
    private readonly StackPanel _uploadPanel = new();
    private readonly TextBox _userName = new();
    private readonly TextBox _password = new() { PasswordChar = '•' };
    private readonly TextBlock _loginState = new();
    private readonly TextBlock _identity = new();
    private readonly TextBlock _selection = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 12 };
    private readonly Button _uploadButton = new();
    private readonly Button _chooseFilesButton = new();
    private readonly Button _chooseFolderButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _logoutButton = new();
    private readonly Button _languageButton = new();

    private CancellationTokenSource? _uploadCts;
    private bool _arabic;

    public MainWindow()
    {
        Title = "Diwan Al Amiri MAM Uploader · macOS";
        Width = 780;
        Height = 700;
        MinWidth = 640;
        MinHeight = 560;
        Background = Brush("#F5F7FA");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _languageButton.Click += (_, _) =>
        {
            _arabic = !_arabic;
            ApplyLanguage();
        };

        BuildLogin();
        BuildUploader();

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        var brandCopy = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = BrandTokens.OrganizationEnglish.ToUpperInvariant(),
                    Foreground = Brush(BrandTokens.Gold600),
                    FontSize = 12,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = BrandTokens.OrganizationArabic,
                    Foreground = Brush("#E7EDF5"),
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Name = "HeaderTitle",
                            Text = "MAM Uploader",
                            Foreground = Brushes.White,
                            FontSize = 23,
                            FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new Border
                        {
                            Background = Brush("#12365A"),
                            BorderBrush = Brush(BrandTokens.Gold500),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(999),
                            Padding = new Thickness(10, 4),
                            Child = new TextBlock
                            {
                                Text = "⌘  macOS",
                                Foreground = Brushes.White,
                                FontSize = 12,
                                FontWeight = FontWeight.Bold
                            }
                        }
                    }
                }
            }
        };

        var header = new Border
        {
            Background = Brush(BrandTokens.Navy950),
            Padding = new Thickness(24, 18),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    Place(BrandCrest(), 0),
                    Place(brandCopy, 1),
                    Place(_languageButton, 2)
                }
            }
        };

        var content = new ScrollViewer
        {
            Padding = new Thickness(32, 28),
            Content = new StackPanel
            {
                MaxWidth = 700,
                Spacing = 18,
                Children = { _loginPanel, _uploadPanel }
            }
        };

        Grid.SetRow(content, 1);
        root.Children.Add(header);
        root.Children.Add(content);
        Content = root;

        _uploadPanel.IsVisible = false;
        ApplyLanguage();
        Closed += (_, _) => _session.Dispose();
    }

    private void BuildLogin()
    {
        _userName.Watermark = "DA\\username";
        _userName.Padding = new Thickness(12, 10);
        _password.Padding = new Thickness(12, 10);

        var signIn = PrimaryButton("Sign in");
        signIn.Click += async (_, _) =>
        {
            signIn.IsEnabled = false;
            _loginState.Text = _arabic ? "جاري تسجيل الدخول…" : "Signing in…";
            try
            {
                var password = _password.Text ?? string.Empty;
                var user = await _session.LoginAsync(_userName.Text ?? string.Empty, password);
                _password.Text = string.Empty;
                _identity.Text = (_arabic ? "متصل باسم: " : "Connected as: ") + user;
                _loginPanel.IsVisible = false;
                _uploadPanel.IsVisible = true;
                _loginState.Text = string.Empty;
            }
            catch (Exception ex)
            {
                _password.Text = string.Empty;
                _loginState.Text = (_arabic ? "فشل تسجيل الدخول: " : "Sign-in failed: ") + Friendly(ex);
            }
            finally
            {
                signIn.IsEnabled = true;
            }
        };

        _loginPanel.Spacing = 14;
        _loginPanel.Children.Add(Heading("Secure Production Sign-in"));
        _loginPanel.Children.Add(Body("Use your Diwan Active Directory account. Domain join is not required. The password is used only for this sign-in request and is not stored."));
        _loginPanel.Children.Add(Label("User name"));
        _loginPanel.Children.Add(_userName);
        _loginPanel.Children.Add(Label("Password"));
        _loginPanel.Children.Add(_password);
        _loginPanel.Children.Add(signIn);
        _loginPanel.Children.Add(StateText(_loginState));
        WrapCard(_loginPanel);
    }

    private void BuildUploader()
    {
        _chooseFilesButton.Click += async (_, _) => await ChooseFilesAsync();
        _chooseFolderButton.Click += async (_, _) => await ChooseFolderAsync();
        _clearButton.Click += (_, _) =>
        {
            if (_uploadCts is not null) return;
            _selectedFiles.Clear();
            _progress.Value = 0;
            _status.Text = string.Empty;
            RefreshSelection();
        };

        _logoutButton.Click += async (_, _) =>
        {
            if (_uploadCts is not null) return;
            await _session.LogoutAsync();
            _selectedFiles.Clear();
            RefreshSelection();
            _uploadPanel.IsVisible = false;
            _loginPanel.IsVisible = true;
            _identity.Text = string.Empty;
        };

        _uploadButton.Click += async (_, _) => await UploadAsync();

        var controls = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = double.NaN,
            ItemHeight = double.NaN
        };
        controls.Children.Add(_chooseFilesButton);
        controls.Children.Add(_chooseFolderButton);
        controls.Children.Add(_clearButton);

        _uploadPanel.Spacing = 14;
        _uploadPanel.Children.Add(Heading("Upload media"));
        _uploadPanel.Children.Add(_identity);
        _uploadPanel.Children.Add(Body("Files go directly to the authoritative Production MAM service. Uploads are resumable during this app session and are SHA-256 verified by the server before Primary promotion."));
        _uploadPanel.Children.Add(controls);
        _uploadPanel.Children.Add(_selection);
        _uploadPanel.Children.Add(_progress);
        _uploadPanel.Children.Add(StateText(_status));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        actions.Children.Add(_uploadButton);
        actions.Children.Add(_logoutButton);
        _uploadPanel.Children.Add(actions);

        WrapCard(_uploadPanel);
    }

    private async Task ChooseFilesAsync()
    {
        var provider = StorageProvider;
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _arabic ? "اختر ملفات الميديا" : "Choose media files",
            AllowMultiple = true
        });

        AddPaths(files.Select(file => file.Path.LocalPath));
    }

    private async Task ChooseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _arabic ? "اختر فولدر الميديا" : "Choose media folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        AddPaths(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(path => File.Exists(path) && MacUploaderSession.IsSupported(path)))
        {
            if (!_selectedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                _selectedFiles.Add(path);
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (_selectedFiles.Count == 0)
        {
            _selection.Text = _arabic ? "لم يتم اختيار أي ميديا بعد." : "No media selected yet.";
            _uploadButton.IsEnabled = false;
            return;
        }

        var totalBytes = _selectedFiles.Sum(path => new FileInfo(path).Length);
        var preview = string.Join(Environment.NewLine, _selectedFiles.Take(6).Select(Path.GetFileName));
        if (_selectedFiles.Count > 6)
            preview += Environment.NewLine + (_arabic ? $"… و {_selectedFiles.Count - 6} ملفات أخرى" : $"… and {_selectedFiles.Count - 6} more files");

        _selection.Text = (_arabic
            ? $"{_selectedFiles.Count} ملف · {FormatBytes(totalBytes)}"
            : $"{_selectedFiles.Count} files · {FormatBytes(totalBytes)}") + Environment.NewLine + preview;
        _uploadButton.IsEnabled = _uploadCts is null;
    }

    private async Task UploadAsync()
    {
        if (_selectedFiles.Count == 0 || _uploadCts is not null) return;

        SetBusy(true);
        _uploadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<MacUploadProgress>(p =>
            {
                _progress.Value = p.OverallPercent;
                var filePercent = p.FileLength <= 0 ? 0 : p.FileBytes * 100d / p.FileLength;
                _status.Text = _arabic
                    ? $"{p.Stage} · ملف {p.FileIndex}/{p.FileCount} · {p.FileName} · {filePercent:0}%"
                    : $"{p.Stage} · file {p.FileIndex}/{p.FileCount} · {p.FileName} · {filePercent:0}%";
            });

            await _session.UploadFilesAsync(_selectedFiles, progress, _uploadCts.Token);
            _progress.Value = 100;
            _status.Text = _arabic
                ? $"تم رفع واعتماد {_selectedFiles.Count} ملف بنجاح في Production."
                : $"{_selectedFiles.Count} file(s) uploaded and verified in Production.";
            _selectedFiles.Clear();
            RefreshSelection();
        }
        catch (OperationCanceledException)
        {
            _status.Text = _arabic ? "تم إيقاف الرفع. يمكن الاستكمال من نفس الجلسة." : "Upload stopped. It can be resumed in this app session.";
        }
        catch (Exception ex)
        {
            _status.Text = (_arabic ? "توقف الرفع: " : "Upload stopped: ") + Friendly(ex);
        }
        finally
        {
            _uploadCts.Dispose();
            _uploadCts = null;
            SetBusy(false);
            RefreshSelection();
        }
    }

    private void SetBusy(bool busy)
    {
        _chooseFilesButton.IsEnabled = !busy;
        _chooseFolderButton.IsEnabled = !busy;
        _clearButton.IsEnabled = !busy;
        _logoutButton.IsEnabled = !busy;
        _uploadButton.IsEnabled = !busy && _selectedFiles.Count > 0;
    }

    private void ApplyLanguage()
    {
        FlowDirection = _arabic ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        _languageButton.Content = _arabic ? "English" : "العربية";
        _chooseFilesButton.Content = _arabic ? "اختيار ملفات" : "Choose files";
        _chooseFolderButton.Content = _arabic ? "اختيار فولدر" : "Choose folder";
        _clearButton.Content = _arabic ? "مسح الاختيار" : "Clear";
        _uploadButton.Content = _arabic ? "بدء / استكمال الرفع" : "Start / resume upload";
        _logoutButton.Content = _arabic ? "تسجيل الخروج" : "Sign out";
        if (_session.IsAuthenticated && !string.IsNullOrWhiteSpace(_session.UserName))
            _identity.Text = (_arabic ? "متصل باسم: " : "Connected as: ") + _session.UserName;
        RefreshSelection();
    }

    private static Border WrapCard(StackPanel panel)
    {
        var children = panel.Children.ToArray();
        panel.Children.Clear();
        var inner = new StackPanel { Spacing = 14 };
        foreach (var child in children) inner.Children.Add(child);
        panel.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D9E2EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = inner
        });
        return (Border)panel.Children[0];
    }

    private static Image BrandCrest()
    {
        var stream = new MemoryStream(DiwanCrestData.Bytes.ToArray(), writable: false);
        return new Image
        {
            Source = new Bitmap(stream),
            Width = 70,
            Height = 70,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("#07182E")
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#475467"),
        LineHeight = 22
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("#344054")
    };

    private static TextBlock StateText(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.Foreground = Brush("#344054");
        return block;
    }

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(18, 10),
        Background = Brush("#B58A2A"),
        Foreground = Brushes.White
    };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static string Friendly(Exception ex)
    {
        var message = ex.Message;
        if (message.Length > 300) message = message[..300] + "…";
        return message;
    }

    private static string FormatBytes(long value)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
