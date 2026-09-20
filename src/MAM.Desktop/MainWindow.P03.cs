using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.Clients;
using MAM.Application.Processing;
using MAM.Application.Uploads;
using Microsoft.Win32;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly HashSet<string> P12VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mxf", ".mov", ".mp4", ".mkv", ".avi", ".webm", ".m4v" };
    private static readonly HashSet<string> P12AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wma" };
    private static readonly HashSet<string> P12ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp" };
    private static readonly HashSet<string> P12DocumentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".rtf", ".txt", ".odt" };

    private MamUploadApiClient? _p03UploadClient;
    private HttpClient? _p03HttpClient;
    private string? _p03SelectedFile;
    private Guid? _p03ActiveSessionId;

    private void InitializeP03UploadIntegration()
    {
        if (_p03UploadClient is not null) return;
        _p03HttpClient = DesktopProductionTransport.CreateApiClient(TimeSpan.FromMinutes(10));
        if (_p03HttpClient is null) return;
        _p03UploadClient = new MamUploadApiClient(
            _p03HttpClient,
            "WindowsDesktop",
            DesktopProductionTransport.DevelopmentUser);

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button =>
                     string.Equals(button.Tag as string, "upload", StringComparison.OrdinalIgnoreCase)))
            button.Click += P03UploadNavigate_Click;
        LanguageButton.Click += P03LanguageChanged_Click;
        if (string.Equals(_currentRoute, "upload", StringComparison.OrdinalIgnoreCase)) ShowP03UploadWorkspace();
    }

    private void P03UploadNavigate_Click(object sender, RoutedEventArgs e)
    {
        _bulkWorkspaceVisible = false;
        ShowP03UploadWorkspace();
    }

    private void P03LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentRoute, "upload", StringComparison.OrdinalIgnoreCase)) return;
        if (_bulkWorkspaceVisible) ShowBulkImportWorkspace();
        else ShowP03UploadWorkspace();
    }

    private void ShowP03UploadWorkspace()
    {
        _bulkWorkspaceVisible = false;
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

        var singleTab = new Button
        {
            Content = _arabic ? "إضافة ميديا واحدة" : "Add one media",
            Margin = new Thickness(0, 0, 8, 10),
            Padding = new Thickness(18, 10, 18, 10),
            Background = Navy(),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            IsEnabled = false
        };
        var bulkButton = new Button
        {
            Content = _arabic ? "إضافة فولدر كامل" : "Add complete folder",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(18, 10, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        bulkButton.Click += (_, _) => ShowBulkImportWorkspace();

        browseButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = false,
                Filter = "Supported media|*.mxf;*.mov;*.mp4;*.mkv;*.avi;*.webm;*.m4v;*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.webp;*.pdf;*.doc;*.docx;*.rtf;*.txt;*.odt|Video|*.mxf;*.mov;*.mp4;*.mkv;*.avi;*.webm;*.m4v|Audio|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma|Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.webp|Documents|*.pdf;*.doc;*.docx;*.rtf;*.txt;*.odt"
            };
            if (dialog.ShowDialog(this) != true) return;
            var extension = Path.GetExtension(dialog.FileName);
            if (!P12IsAllowedExtension(extension))
            {
                stateText.Text = _arabic ? "نوع الملف غير مسموح به." : "The selected file type is not allowed.";
                return;
            }
            _p03SelectedFile = dialog.FileName;
            _p03ActiveSessionId = null;
            fileText.Text = Path.GetFileName(dialog.FileName);
            if (string.IsNullOrWhiteSpace(titleInput.Text)) titleInput.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            stateText.Text = _arabic ? "تم اختيار الملف. جاهز للبدء." : "File selected. Ready to start.";
        };

        uploadButton.Click += async (_, _) =>
        {
            var selectedPath = _p03SelectedFile;
            if (_p03UploadClient is null || string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
            {
                stateText.Text = _arabic ? "اختر ملفًا صالحًا أولاً." : "Choose a valid file first.";
                return;
            }
            var extension = Path.GetExtension(selectedPath);
            if (!P12IsAllowedExtension(extension))
            {
                stateText.Text = _arabic ? "نوع الملف غير مسموح به." : "The selected file type is not allowed.";
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
                var file = new FileInfo(selectedPath);
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
                _p12SelectedAssetId = finalized.AssetId;
                stateText.Text = _arabic ? "تم اعتماد الأصل؛ جاري إضافة المعالجة والفهرسة تلقائيًا…" : "Primary verified; queueing automatic processing and indexing…";
                var profiles = await QueueP12AutomaticProcessingAsync(finalized.AssetId, extension);
                stateText.Text = _arabic
                    ? $"تم اعتماد الأصل. SHA-256: {finalized.Sha256[..16]}…\nالمعالجة المدرجة: {string.Join("، ", profiles)}"
                    : $"Primary original verified. SHA-256: {finalized.Sha256[..16]}…\nQueued processing: {string.Join(", ", profiles)}";
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                stateText.Text = _arabic ? "لا توجد صلاحية للرفع أو المعالجة لهذا النوع." : "Permission denied for upload or processing of this media type.";
            }
            catch (MamApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                stateText.Text = _arabic ? "التخزين أو خدمة المعالجة في حالة Degraded. الأصل المعتمد يبقى محفوظًا ويمكن إدراج المعالجة لاحقًا." : "Storage or processing is degraded. Any promoted Primary original remains durable and processing can be queued later.";
            }
            catch (Exception ex) when (ex is MamApiException or HttpRequestException or TaskCanceledException or IOException)
            {
                stateText.Text = _arabic
                    ? "توقف الرفع أو المعالجة التلقائية. الإزاحة المؤكدة لا تضيع، والأصل المعتمد يبقى محفوظًا."
                    : "Upload or automatic processing paused. The acknowledged offset is preserved, and any promoted Primary original remains durable.";
            }
            finally
            {
                browseButton.IsEnabled = true;
                uploadButton.IsEnabled = true;
            }
        };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(singleTab);
        tabs.Children.Add(bulkButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(browseButton);
        actions.Children.Add(uploadButton);
        var uploadCard = new StackPanel();
        uploadCard.Children.Add(tabs);
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
        uploadCard.Children.Add(new TextBlock
        {
            Text = _arabic
                ? "المسموح: فيديو MXF/MOV/MP4/MKV/AVI/WEBM/M4V · صوت WAV/MP3/M4A/AAC/FLAC/OGG/WMA · صور JPG/PNG/TIFF/BMP/WEBP · مستندات PDF/DOC/DOCX/RTF/TXT/ODT."
                : "Allowed: video MXF/MOV/MP4/MKV/AVI/WEBM/M4V · audio WAV/MP3/M4A/AAC/FLAC/OGG/WMA · images JPG/PNG/TIFF/BMP/WEBP · documents PDF/DOC/DOCX/RTF/TXT/ODT.",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text()
        });
        uploadCard.Children.Add(titleInput);
        uploadCard.Children.Add(fileText);
        uploadCard.Children.Add(actions);
        uploadCard.Children.Add(stateText);

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "رفع الملفات" : "Upload Workspace", _arabic
                ? "يتم إدراج التفريغ/OCR والفهرسة تلقائيًا بعد اعتماد الأصل."
                : "Transcript/OCR extraction and indexing are queued automatically after Primary verification."),
            Card(string.Empty, uploadCard),
            StateCard("Degraded / Retry", _arabic
                ? "عند انقطاع الشبكة تبقى الإزاحة المؤكدة على الخادم ويمكن الاستكمال."
                : "On network interruption, the acknowledged server offset remains resumable.", "#FFFAEB", "#B54708")));
    }

    private async Task<IReadOnlyList<string>> QueueP12AutomaticProcessingAsync(Guid assetId, string extension)
    {
        if (_p04ProcessingClient is null) return Array.Empty<string>();
        var profiles = new List<string>();
        if (P12VideoExtensions.Contains(extension) || P12AudioExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.TranscriptText);
        }
        else if (P12ImageExtensions.Contains(extension))
        {
            profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }
        else if (P12DocumentExtensions.Contains(extension))
        {
            if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)) profiles.Add(BuiltInProcessingProfiles.Inspect);
            profiles.Add(BuiltInProcessingProfiles.OcrText);
        }

        foreach (var profile in profiles) await _p04ProcessingClient.EnqueueAsync(assetId, profile);
        return profiles;
    }

    private static bool P12IsAllowedExtension(string extension) =>
        P12VideoExtensions.Contains(extension) || P12AudioExtensions.Contains(extension) || P12ImageExtensions.Contains(extension) || P12DocumentExtensions.Contains(extension);

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream)).ToLowerInvariant();
    }
}
