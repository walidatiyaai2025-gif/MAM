using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MAM.Application.BulkImport;
using MAM.Application.Clients;
using Microsoft.Win32;

namespace MAM.Desktop;

public partial class MainWindow
{
    private MamBulkImportApiClient? _bulkImportClient;
    private string? _bulkRootPath;
    private Guid? _bulkSessionId;
    private bool _bulkWorkspaceVisible;
    private CancellationTokenSource? _bulkRunCancellation;
    private IReadOnlyDictionary<string, BulkLocalFile>? _bulkLocalFiles;

    private void ShowBulkImportWorkspace()
    {
        _bulkWorkspaceVisible = true;
        EnsureBulkImportClient();
        if (_bulkImportClient is null || _p03UploadClient is null)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "استيراد مجلدات مجمّع" : "Bulk Folder Import",
                    _arabic ? "الخدمة المركزية غير مهيأة لهذا العميل." : "The Central API is not configured for this client."),
                StateCard(_arabic ? "الخدمة غير متاحة" : "Service unavailable",
                    _arabic ? "تعذر تهيئة اتصال الإنتاج قبل الاستيراد." : "Production connection could not be initialized for bulk import.",
                    "#FEF3F2", "#B42318")));
            return;
        }

        var rootText = new TextBlock
        {
            Text = _bulkRootPath ?? (_arabic ? "لم يتم اختيار مجلد." : "No folder selected."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var stateText = new TextBlock
        {
            Text = _arabic
                ? "اختر مجلدًا. المجلد المختار يصبح التصنيف الرئيسي، وكل مجلد تحته مباشرة يصبح تصنيفًا فرعيًا وتُرفع الميديا عليه."
                : "Select a folder. The selected folder becomes the main category; each immediate child folder becomes a subcategory and receives its media.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text(),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var statsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var currentText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var overall = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Margin = new Thickness(0, 10, 0, 0) };
        var current = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(0, 6, 0, 0) };
        var resultsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Text(),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var browse = BulkButton(_arabic ? "اختيار مجلد" : "Browse folder", false);
        var start = BulkButton(_arabic ? "فحص وبدء الاستيراد" : "Scan & start import", true);
        var resume = BulkButton(_arabic ? "استكمال / إعادة محاولة الفاشل" : "Resume / retry failed", false);
        var pause = BulkButton(_arabic ? "إيقاف مؤقت" : "Pause", false);
        var cancel = BulkButton(_arabic ? "إلغاء الجلسة" : "Cancel session", false);
        var report = BulkButton(_arabic ? "حفظ TXT + CSV" : "Save TXT + CSV", false);
        var singleTab = BulkButton(_arabic ? "إضافة ميديا واحدة" : "Add one media", false);
        var folderTab = BulkButton(_arabic ? "إضافة فولدر كامل" : "Add complete folder", true);
        folderTab.IsEnabled = false;

        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = _arabic ? "اختر مجلد الميديا" : "Select media root folder",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            _bulkRootPath = dialog.FolderName;
            _bulkLocalFiles = null;
            rootText.Text = _bulkRootPath;
            stateText.Text = _arabic ? "تم اختيار المجلد. اضغط فحص وبدء الاستيراد." : "Folder selected. Choose Scan & start import.";
        };

        start.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_bulkRootPath) || !Directory.Exists(_bulkRootPath))
            {
                stateText.Text = _arabic ? "اختر مجلدًا صالحًا أولًا." : "Select a valid folder first.";
                return;
            }

            SetBulkButtons(false, browse, start, resume);
            try
            {
                stateText.Text = _arabic ? "جاري فحص الملفات وحساب SHA-256…" : "Scanning files and calculating SHA-256…";
                _bulkLocalFiles = await BuildBulkLocalManifestAsync(_bulkRootPath, (done, total, path) =>
                {
                    stateText.Text = $"{(_arabic ? "فحص" : "Scanning")} {done}/{total} · {path}";
                    overall.Value = total == 0 ? 0 : done * 100d / total;
                });
                var descriptors = _bulkLocalFiles.Values
                    .Select(x => new BulkImportFileDescriptor(x.RelativePath, x.Length, x.Sha256))
                    .ToArray();
                var session = await _bulkImportClient!.CreateSessionAsync(
                    new CreateBulkImportSessionRequest(Path.GetFileName(Path.TrimEndingDirectorySeparator(_bulkRootPath)), descriptors));
                _bulkSessionId = session.SessionId;
                UpdateBulkUi(session, overall, statsText, resultsText);
                await RunBulkImportAsync(session, _bulkRootPath, overall, current, stateText, statsText, currentText, resultsText);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MamApiException or HttpRequestException)
            {
                stateText.Text = $"{(_arabic ? "تعذر بدء الاستيراد" : "Import could not start")}: {ex.Message}";
            }
            finally
            {
                SetBulkButtons(true, browse, start, resume);
            }
        };

        resume.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_bulkRootPath) || !Directory.Exists(_bulkRootPath))
            {
                stateText.Text = _arabic ? "اختر نفس المجلد المحلي أولًا ثم اضغط استكمال." : "Select the same local folder first, then resume.";
                return;
            }

            SetBulkButtons(false, browse, start, resume);
            try
            {
                var recent = await _bulkImportClient!.ListRecentAsync(20);
                var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_bulkRootPath));
                var candidate = recent.FirstOrDefault(x =>
                    string.Equals(x.RootFolderName, rootName, StringComparison.OrdinalIgnoreCase) &&
                    x.State is BulkImportSessionState.Ready or BulkImportSessionState.Running or BulkImportSessionState.CompletedWithErrors);
                if (candidate is null)
                {
                    stateText.Text = _arabic ? "لا توجد جلسة قابلة للاستكمال لهذا المجلد." : "No resumable session exists for this folder.";
                    return;
                }

                _bulkSessionId = candidate.SessionId;
                stateText.Text = _arabic ? "جاري إعادة فحص الملفات المحلية قبل الاستكمال…" : "Re-scanning local files before resume…";
                _bulkLocalFiles = await BuildBulkLocalManifestAsync(_bulkRootPath, (done, total, path) =>
                {
                    stateText.Text = $"{(_arabic ? "فحص" : "Scanning")} {done}/{total} · {path}";
                });
                var session = await _bulkImportClient.GetSessionAsync(candidate.SessionId);
                UpdateBulkUi(session, overall, statsText, resultsText);
                await RunBulkImportAsync(session, _bulkRootPath, overall, current, stateText, statsText, currentText, resultsText);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MamApiException or HttpRequestException)
            {
                stateText.Text = $"{(_arabic ? "تعذر الاستكمال" : "Resume failed")}: {ex.Message}";
            }
            finally
            {
                SetBulkButtons(true, browse, start, resume);
            }
        };

        pause.Click += (_, _) =>
        {
            _bulkRunCancellation?.Cancel();
            stateText.Text = _arabic
                ? "تم الإيقاف مؤقتًا. الإزاحة المؤكدة محفوظة على الخادم."
                : "Paused. The acknowledged server offset is preserved.";
        };

        cancel.Click += async (_, _) =>
        {
            if (_bulkSessionId is not Guid sessionId) return;
            try
            {
                _bulkRunCancellation?.Cancel();
                var snapshot = await _bulkImportClient!.CancelAsync(sessionId);
                UpdateBulkUi(snapshot, overall, statsText, resultsText);
                stateText.Text = _arabic ? "تم إلغاء جلسة الاستيراد." : "Bulk import session cancelled.";
            }
            catch (MamApiException ex)
            {
                stateText.Text = $"{(_arabic ? "فشل الإلغاء" : "Cancel failed")}: {ex.Message}";
            }
        };

        report.Click += async (_, _) =>
        {
            if (_bulkSessionId is not Guid sessionId) return;
            try
            {
                var paths = await SaveBulkReportsAsync(sessionId);
                stateText.Text = _arabic
                    ? $"تم حفظ التقريرين:\n{paths.Txt}\n{paths.Csv}"
                    : $"Reports saved:\n{paths.Txt}\n{paths.Csv}";
            }
            catch (Exception ex) when (ex is IOException or MamApiException or HttpRequestException)
            {
                stateText.Text = $"{(_arabic ? "تعذر حفظ التقرير" : "Report save failed")}: {ex.Message}";
            }
        };

        singleTab.Click += (_, _) =>
        {
            _bulkWorkspaceVisible = false;
            ShowP03UploadWorkspace();
        };

        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        tabs.Children.Add(singleTab);
        tabs.Children.Add(folderTab);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in new[] { browse, start, resume, pause, cancel, report }) actions.Children.Add(button);

        var selection = new StackPanel();
        selection.Children.Add(tabs);
        selection.Children.Add(new TextBlock
        {
            Text = _arabic ? "مجلد المصدر" : "Source folder",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Navy()
        });
        selection.Children.Add(rootText);
        selection.Children.Add(actions);
        selection.Children.Add(stateText);

        var progress = new StackPanel();
        progress.Children.Add(new TextBlock
        {
            Text = _arabic ? "التقدم الكلي" : "Overall progress",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Navy()
        });
        progress.Children.Add(overall);
        progress.Children.Add(statsText);
        progress.Children.Add(new TextBlock
        {
            Text = _arabic ? "الملف الحالي" : "Current file",
            Margin = new Thickness(0, 14, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = Navy()
        });
        progress.Children.Add(currentText);
        progress.Children.Add(current);
        progress.Children.Add(resultsText);

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "استيراد مجلدات مجمّع" : "Bulk Folder Import",
                _arabic
                    ? "رفع متين قابل للاستكمال مع إنشاء التصنيفات، منع التكرار، وتقرير TXT/CSV."
                    : "Durable resumable folder import with category creation, duplicate prevention, and TXT/CSV reporting."),
            Card(string.Empty, selection),
            Card(string.Empty, progress),
            StateCard(_arabic ? "قاعدة التصنيف" : "Category rule",
                _arabic
                    ? "المجلد المختار هو التصنيف الرئيسي. كل مجلد تحته مباشرة يصبح تصنيفًا فرعيًا، والملفات الموجودة مباشرة في الجذر تبقى على التصنيف الرئيسي."
                    : "The selected folder is the main category. Each immediate child folder becomes a subcategory; files directly in the root stay on the main category.",
                "#EFF8FF", "#175CD3")));
    }

    private void EnsureBulkImportClient()
    {
        if (_bulkImportClient is not null) return;
        if (_p03HttpClient is null || _p03UploadClient is null) InitializeP03UploadIntegration();
        if (_p03HttpClient is null) return;
        _bulkImportClient = new MamBulkImportApiClient(
            _p03HttpClient,
            "WindowsDesktop",
            DesktopProductionTransport.DevelopmentUser);
    }

    private async Task<IReadOnlyDictionary<string, BulkLocalFile>> BuildBulkLocalManifestAsync(
        string root,
        Action<int, int, string>? progress = null)
    {
        var paths = await Task.Run(() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray());
        var result = new Dictionary<string, BulkLocalFile>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var info = new FileInfo(path);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            string? sha = null;
            if (P12IsAllowedExtension(info.Extension) && info.Length > 0)
                sha = await ComputeFileSha256Async(path);
            result[relative] = new BulkLocalFile(relative, path, info.Length, sha);
            progress?.Invoke(index + 1, paths.Length, relative);
        }
        return result;
    }

    private async Task RunBulkImportAsync(
        BulkImportSessionSnapshot initial,
        string root,
        ProgressBar overall,
        ProgressBar current,
        TextBlock stateText,
        TextBlock statsText,
        TextBlock currentText,
        TextBlock resultsText)
    {
        if (_bulkImportClient is null || _p03UploadClient is null) return;
        _bulkRunCancellation?.Dispose();
        _bulkRunCancellation = new CancellationTokenSource();
        var cancellationToken = _bulkRunCancellation.Token;
        var snapshot = initial;
        var local = _bulkLocalFiles ?? await BuildBulkLocalManifestAsync(root);
        UpdateBulkUi(snapshot, overall, statsText, resultsText);

        foreach (var sourceItem in snapshot.Items.Where(x =>
                     x.State is BulkImportItemState.Pending or BulkImportItemState.Uploading or BulkImportItemState.Failed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!local.TryGetValue(sourceItem.RelativePath, out var localFile))
            {
                await _bulkImportClient.FailItemAsync(snapshot.SessionId, sourceItem.ItemId, "local_file_missing",
                    "The source file is not present in the selected local folder.", cancellationToken);
                snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, cancellationToken);
                UpdateBulkUi(snapshot, overall, statsText, resultsText);
                continue;
            }

            currentText.Text = sourceItem.RelativePath;
            current.Value = 0;

            try
            {
                if (sourceItem.ExpectedLength != localFile.Length ||
                    (sourceItem.ExpectedSha256 is not null && !string.Equals(sourceItem.ExpectedSha256, localFile.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    await _bulkImportClient.FailItemAsync(snapshot.SessionId, sourceItem.ItemId, "local_file_changed",
                        "Local file length or SHA-256 changed since the import manifest was created.", cancellationToken);
                    snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, cancellationToken);
                    UpdateBulkUi(snapshot, overall, statsText, resultsText);
                    continue;
                }

                var item = await _bulkImportClient.BeginItemAsync(snapshot.SessionId, sourceItem.ItemId, cancellationToken);
                if (item.State is BulkImportItemState.AlreadyExists or BulkImportItemState.Linked or BulkImportItemState.Uploaded)
                {
                    snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, cancellationToken);
                    UpdateBulkUi(snapshot, overall, statsText, resultsText);
                    continue;
                }
                if (item.UploadSessionId is not Guid uploadSessionId)
                    throw new InvalidOperationException("Bulk import item did not return a durable upload session.");

                var upload = await _p03UploadClient.GetSessionAsync(uploadSessionId, cancellationToken);
                await using var source = new FileStream(localFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var offset = upload.ReceivedLength;
                source.Position = offset;
                var buffer = new byte[upload.Session.ChunkSizeBytes];

                while (offset < localFile.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = (int)Math.Min(buffer.Length, localFile.Length - offset);
                    var read = 0;
                    while (read < requested)
                    {
                        var count = await source.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken);
                        if (count == 0) break;
                        read += count;
                    }
                    if (read == 0) throw new EndOfStreamException("Local source ended before the declared file length.");

                    var chunk = buffer.AsMemory(0, read).ToArray();
                    var chunkSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(chunk)).ToLowerInvariant();
                    await using var chunkStream = new MemoryStream(chunk, writable: false);
                    var receipt = await _p03UploadClient.PutChunkAsync(uploadSessionId, offset, chunkSha, chunkStream, cancellationToken);
                    offset = receipt.ReceivedLength;
                    current.Value = localFile.Length == 0 ? 100 : offset * 100d / localFile.Length;
                    stateText.Text = $"{(_arabic ? "رفع" : "Uploading")} · {sourceItem.RelativePath} · {current.Value:0}%";
                    overall.Value = snapshot.TotalFiles == 0
                        ? 0
                        : Math.Min(100, (snapshot.ProcessedFiles + current.Value / 100d) * 100d / snapshot.TotalFiles);
                }

                var finalized = await _bulkImportClient.FinalizeItemAsync(snapshot.SessionId, sourceItem.ItemId, cancellationToken);
                current.Value = finalized.State is BulkImportItemState.Uploaded or BulkImportItemState.AlreadyExists or BulkImportItemState.Linked ? 100 : current.Value;
                snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, cancellationToken);
                UpdateBulkUi(snapshot, overall, statsText, resultsText);
            }
            catch (OperationCanceledException)
            {
                stateText.Text = _arabic
                    ? "تم الإيقاف مؤقتًا. يمكن الاستكمال من نفس الإزاحة."
                    : "Paused. Resume will continue from the acknowledged offset.";
                return;
            }
            catch (Exception ex) when (ex is IOException or MamApiException or HttpRequestException or InvalidOperationException)
            {
                try
                {
                    await _bulkImportClient.FailItemAsync(snapshot.SessionId, sourceItem.ItemId, "client_upload_failed", ex.Message, CancellationToken.None);
                }
                catch { }
                stateText.Text = $"{(_arabic ? "توقف الملف الحالي" : "Current file failed")}: {ex.Message}";
                snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
                UpdateBulkUi(snapshot, overall, statsText, resultsText);
                if (ex is HttpRequestException) return;
            }
        }

        snapshot = await _bulkImportClient.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        UpdateBulkUi(snapshot, overall, statsText, resultsText);
        if (snapshot.State is BulkImportSessionState.Completed or BulkImportSessionState.CompletedWithErrors)
        {
            var paths = await SaveBulkReportsAsync(snapshot.SessionId);
            stateText.Text = _arabic
                ? $"اكتملت الجلسة. تم حفظ TXT وCSV تلقائيًا.\n{paths.Txt}\n{paths.Csv}"
                : $"Session completed. TXT and CSV reports were saved automatically.\n{paths.Txt}\n{paths.Csv}";
        }
    }

    private static void SetBulkButtons(bool enabled, params Button[] buttons)
    {
        foreach (var button in buttons) button.IsEnabled = enabled;
    }

    private Button BulkButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Margin = new Thickness(0, 6, 8, 0),
            Padding = new Thickness(13, 8, 13, 8),
            Background = primary ? Gold() : System.Windows.Media.Brushes.White,
            Foreground = primary ? System.Windows.Media.Brushes.White : Navy(),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = primary ? new Thickness(0) : new Thickness(1)
        };
    }

    private void UpdateBulkUi(BulkImportSessionSnapshot snapshot, ProgressBar overall, TextBlock statsText, TextBlock resultsText)
    {
        overall.Value = snapshot.TotalFiles == 0 ? 0 : snapshot.ProcessedFiles * 100d / snapshot.TotalFiles;
        statsText.Text = _arabic
            ? $"{snapshot.ProcessedFiles}/{snapshot.TotalFiles} · مرفوع {snapshot.Uploaded} · موجود {snapshot.AlreadyExists} · أعيد تصنيفه {snapshot.Linked} · فشل {snapshot.Failed} · غير مدعوم {snapshot.Unsupported}"
            : $"{snapshot.ProcessedFiles}/{snapshot.TotalFiles} · Uploaded {snapshot.Uploaded} · Existing {snapshot.AlreadyExists} · Reclassified {snapshot.Linked} · Failed {snapshot.Failed} · Unsupported {snapshot.Unsupported}";
        resultsText.Text = string.Join(Environment.NewLine, snapshot.Items
            .Where(x => x.State is not BulkImportItemState.Pending)
            .TakeLast(24)
            .Select(x => $"{x.State,-14} {x.RelativePath}{(string.IsNullOrWhiteSpace(x.ReasonCode) ? "" : $" · {x.ReasonCode}")}"));
    }

    private async Task<(string Txt, string Csv)> SaveBulkReportsAsync(Guid sessionId)
    {
        if (_bulkImportClient is null) throw new InvalidOperationException("Bulk import client is not configured.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MAM", "Import Reports");
        Directory.CreateDirectory(directory);
        var txt = await _bulkImportClient.DownloadReportAsync(sessionId, "txt");
        var csv = await _bulkImportClient.DownloadReportAsync(sessionId, "csv");
        var txtPath = Path.Combine(directory, Path.GetFileName(txt.FileName));
        var csvPath = Path.Combine(directory, Path.GetFileName(csv.FileName));
        await File.WriteAllTextAsync(txtPath, txt.Content, new System.Text.UTF8Encoding(false));
        await File.WriteAllTextAsync(csvPath, csv.Content, new System.Text.UTF8Encoding(false));
        return (txtPath, csvPath);
    }

    private sealed record BulkLocalFile(string RelativePath, string FullPath, long Length, string? Sha256);
}
