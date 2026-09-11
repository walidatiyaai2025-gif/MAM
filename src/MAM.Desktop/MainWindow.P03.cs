using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Clients;
using MAM.Application.Uploads;
using Microsoft.Win32;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamUploadApiClient? _p03UploadClient;
    private HttpClient? _p03HttpClient;
    private string? _p03SelectedFile;
    private Guid? _p03ActiveSessionId;

    private void InitializeP03UploadIntegration()
    {
        if (_p03UploadClient is not null) return;
        var apiBase = Environment.GetEnvironmentVariable("MAM_API_BASE_URL");
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri)) return;

        _p03HttpClient = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(apiUri),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _p03UploadClient = new MamUploadApiClient(
            _p03HttpClient,
            "WindowsDesktop",
            Environment.GetEnvironmentVariable("MAM_DEV_USER"));

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button =>
                     string.Equals(button.Tag as string, "upload", StringComparison.OrdinalIgnoreCase)))
            button.Click += P03UploadNavigate_Click;
        LanguageButton.Click += P03LanguageChanged_Click;
        if (string.Equals(_currentRoute, "upload", StringComparison.OrdinalIgnoreCase)) ShowP03UploadWorkspace();
    }

    private void P03UploadNavigate_Click(object sender, RoutedEventArgs e) => ShowP03UploadWorkspace();

    private void P03LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentRoute, "upload", StringComparison.OrdinalIgnoreCase)) ShowP03UploadWorkspace();
    }

    private void ShowP03UploadWorkspace()
    {
        if (_p03UploadClient is null) return;

        var titleInput = new TextBox
        {
            MaxLength = 300,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = _arabic ? "عنوان الأصل" : "Asset title"
        };
        var fileText = new TextBlock
        {
            Text = _p03SelectedFile is null
                ? (_arabic ? "لم يتم اختيار ملف." : "No file selected.")
                : Path.GetFileName(_p03SelectedFile),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text()
        };
        var stateText = new TextBlock
        {
            Text = _arabic ? "جاهز للرفع القابل للاستكمال." : "Ready for resumable upload.",
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text()
        };
        var browseButton = new Button
        {
            Content = _arabic ? "اختيار ملف" : "Choose file",
            Margin = new Thickness(0, 10, 10, 0),
            Padding = new Thickness(14, 9, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var uploadButton = new Button
        {
            Content = _arabic ? "بدء / استكمال" : "Start / resume",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(14, 9, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Gold(),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };

        browseButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) != true) return;
            _p03SelectedFile = dialog.FileName;
            _p03ActiveSessionId = null;
            fileText.Text = Path.GetFileName(dialog.FileName);
            if (string.IsNullOrWhiteSpace(titleInput.Text)) titleInput.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            stateText.Text = _arabic ? "تم اختيار الملف. جاهز للبدء." : "File selected. Ready to start.";
        };

        uploadButton.Click += async (_, _) =>
        {
            if (_p03UploadClient is null || string.IsNullOrWhiteSpace(_p03SelectedFile) || !File.Exists(_p03SelectedFile))
            {
                stateText.Text = _arabic ? "اختر ملفًا صالحًا أولاً." : "Choose a valid file first.";
                return;
            }
            var title = titleInput.Text.Trim();
            if (title.Length == 0)
            {
                stateText.Text = _arabic ? "العنوان مطلوب." : "Asset title is required.";
                return;
            }

            browseButton.IsEnabled = false;
            uploadButton.IsEnabled = false;
            try
            {
                var file = new FileInfo(_p03SelectedFile);
                stateText.Text = _arabic ? "جاري حساب SHA-256…" : "Calculating SHA-256…";
                var fullSha = await ComputeFileSha256Async(file.FullName);
                UploadSessionSnapshot session;
                if (_p03ActiveSessionId is Guid existingSession)
                {
                    session = await _p03UploadClient.GetSessionAsync(existingSession);
                }
                else
                {
                    session = await _p03UploadClient.CreateSessionAsync(new CreateUploadSessionRequest(
                        title, file.Name, file.Length, fullSha));
                    _p03ActiveSessionId = session.Session.SessionId;
                }

                await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var offset = session.ReceivedLength;
                source.Position = offset;
                var buffer = new byte[session.Session.ChunkSizeBytes];
                while (offset < file.Length)
                {
                    var requested = (int)Math.Min(buffer.Length, file.Length - offset);
                    var read = 0;
                    while (read < requested)
                    {
                        var count = await source.ReadAsync(buffer.AsMemory(read, requested - read));
                        if (count == 0) break;
                        read += count;
                    }
                    if (read == 0) throw new EndOfStreamException("Local source ended before the declared file length.");
                    var chunk = buffer.AsMemory(0, read).ToArray();
                    var chunkSha = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
                    await using var chunkStream = new MemoryStream(chunk, writable: false);
                    var result = await _p03UploadClient.PutChunkAsync(session.Session.SessionId, offset, chunkSha, chunkStream);
                    offset = result.ReceivedLength;
                    source.Position = offset;
                    var percent = (int)Math.Floor(offset * 100d / file.Length);
                    stateText.Text = $"{(_arabic ? "رفع" : "Uploading")} {percent}% · {offset:N0}/{file.Length:N0}";
                }

                stateText.Text = _arabic ? "جاري التحقق النهائي على الخادم…" : "Server is verifying size and SHA-256…";
                var finalized = await _p03UploadClient.FinalizeAsync(session.Session.SessionId);
                _p03ActiveSessionId = null;
                stateText.Text = _arabic
                    ? $"تم اعتماد النسخة الأصلية. SHA-256: {finalized.Sha256[..16]}…"
                    : $"Primary original verified. SHA-256: {finalized.Sha256[..16]}…";
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                stateText.Text = _arabic ? "لا توجد صلاحية للرفع." : "Permission denied for upload.";
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                stateText.Text = _arabic ? "التخزين الأساسي أو الخدمة المركزية في حالة Degraded. يمكن الاستكمال لاحقًا." : "Primary Storage or Central API is degraded. Resume remains available.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException or IOException)
            {
                stateText.Text = _arabic
                    ? "توقف الرفع دون فقد الإزاحة المؤكدة. اضغط بدء / استكمال لإعادة المحاولة."
                    : "Upload paused without losing the acknowledged offset. Use Start / resume to retry.";
            }
            finally
            {
                browseButton.IsEnabled = true;
                uploadButton.IsEnabled = true;
            }
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(browseButton);
        actions.Children.Add(uploadButton);
        var uploadCard = new StackPanel();
        uploadCard.Children.Add(new TextBlock
        {
            Text = _arabic ? "رفع متين قابل للاستكمال" : "Durable resumable upload",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Navy()
        });
        uploadCard.Children.Add(new TextBlock
        {
            Text = _arabic
                ? "الاختيار المحلي مؤقت؛ الخادم يتحقق من الحجم وSHA-256 قبل اعتماد Primary."
                : "Local selection is temporary; the server verifies size and SHA-256 before Primary promotion.",
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text()
        });
        uploadCard.Children.Add(titleInput);
        uploadCard.Children.Add(fileText);
        uploadCard.Children.Add(actions);
        uploadCard.Children.Add(stateText);

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "رفع الملفات" : "Upload Workspace", _arabic
                ? "لا يتم إرسال مسار التخزين الأساسي أو بيانات اعتماده إلى Windows."
                : "Primary Storage paths and credentials are never exposed to Windows."),
            Card(string.Empty, uploadCard),
            StateCard("Degraded / Retry", _arabic
                ? "عند انقطاع الشبكة تبقى الإزاحة المؤكدة على الخادم ويمكن الاستكمال."
                : "On network interruption, the acknowledged server offset remains resumable.", "#FFFAEB", "#B54708")));
    }

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream)).ToLowerInvariant();
    }
}
