using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MAM.Application.Capture;
using MAM.Application.Clients;
using MAM.Application.Diagnostics;
using MAM.Application.Protection;
using MAM.Application.Uploads;
using MAM.Infrastructure.Capture;

namespace MAM.Desktop;

public partial class MainWindow
{
    private static readonly bool P07LoadedHandlerRegistered = RegisterP07LoadedHandler();
    private ICaptureProvider? _p07CaptureProvider;
    private CaptureRecoveryManifestStore? _p07RecoveryStore;
    private CaptureSessionRequest? _p07ActiveRequest;
    private Guid? _p07ActiveSessionId;
    private string? _p07LastTimecode;
    private string? _p07CacheRoot;
    private bool _p07Wired;

    private static bool RegisterP07LoadedHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), LoadedEvent, new RoutedEventHandler(P07WindowLoaded));
        return true;
    }

    private static void P07WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.WireP07CaptureUi();
    }

    private void WireP07CaptureUi()
    {
        if (_p07Wired) return;
        _p07Wired = true;

        _p07CacheRoot = Environment.GetEnvironmentVariable("MAM_CAPTURE_CACHE_ROOT");
        if (string.IsNullOrWhiteSpace(_p07CacheRoot))
        {
            _p07CacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiwanAlAmiri", "MAM", "CaptureCache");
        }
        _p07RecoveryStore = new CaptureRecoveryManifestStore(Path.Combine(_p07CacheRoot, "recovery"));

        var provider = Environment.GetEnvironmentVariable("MAM_CAPTURE_PROVIDER");
        var environment = BuildInfo.Current.EnvironmentName;
        if (string.Equals(provider, "Simulator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            _p07CaptureProvider = new SimulatedCaptureProvider();
        }

        foreach (var button in NavPanel.Children.OfType<Button>().Where(button =>
                     string.Equals(button.Tag as string, "capture", StringComparison.OrdinalIgnoreCase)))
            button.Click += P07CaptureNavigate_Click;

        LanguageButton.Click += P07LanguageChanged_Click;
        if (string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase))
            _ = ShowP07CaptureWorkspaceAsync();
    }

    private async void P07CaptureNavigate_Click(object sender, RoutedEventArgs e) => await ShowP07CaptureWorkspaceAsync();

    private async void P07LanguageChanged_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase))
            await ShowP07CaptureWorkspaceAsync();
    }

    private async Task ShowP07CaptureWorkspaceAsync()
    {
        if (!string.Equals(_currentRoute, "capture", StringComparison.OrdinalIgnoreCase)) return;

        PageTitle.Text = _arabic ? "التسجيل من الشريط" : "Windows Tape Capture";
        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "مساحة التسجيل من الشريط" : "Windows Tape Capture",
                _arabic ? "جاري فحص موفر التسجيل وحالة الاسترداد…" : "Inspecting capture provider and durable recovery state…"),
            StateCard("Loading", _arabic ? "جاري تحميل أجهزة التسجيل…" : "Loading capture devices…", "#EFF8FF", "#175CD3")));

        if (_p07CaptureProvider is null)
        {
            var environment = BuildInfo.Current.EnvironmentName;
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "التسجيل من الشريط" : "Windows Tape Capture",
                    _arabic ? "لا يوجد موفر أجهزة حقيقي معتمد ومهيأ لهذا الجهاز." : "No approved real-hardware capture provider is configured on this workstation."),
                StateCard("OWNER_LAST / Hardware required",
                    _arabic
                        ? "المسار يفشل بشكل آمن. لا يتم استخدام المحاكي في Production ولا يُحتسب كدليل قبول جهاز حقيقي."
                        : "Fail-closed: the simulator is never enabled in Production and never counts as real-hardware acceptance.",
                    "#FFFAEB", "#B54708"),
                Card(_arabic ? "حالة البيئة" : "Environment", new TextBlock
                {
                    Text = $"{environment} · MAM_CAPTURE_PROVIDER={Environment.GetEnvironmentVariable("MAM_CAPTURE_PROVIDER") ?? "<not configured>"}",
                    Foreground = Text(), TextWrapping = TextWrapping.Wrap
                }),
                await BuildP07RecoveryCardAsync()));
            return;
        }

        IReadOnlyList<CaptureDeviceDescriptor> devices;
        try
        {
            devices = await _p07CaptureProvider.GetDevicesAsync(default);
        }
        catch (Exception ex)
        {
            ContentHost.Content = BuildP07Failure(_arabic ? "تعذر اكتشاف أجهزة التسجيل." : "Capture device discovery failed.", ex.Message);
            return;
        }

        if (devices.Count == 0)
        {
            ContentHost.Content = Scroll(PageStack(
                Lead(_arabic ? "التسجيل من الشريط" : "Windows Tape Capture", _arabic ? "لا توجد أجهزة متاحة." : "No capture devices are available."),
                StateCard("Degraded", _arabic ? "لا يمكن بدء التسجيل بدون جهاز معتمد." : "Recording is blocked until an approved device is available.", "#FFFAEB", "#B54708"),
                await BuildP07RecoveryCardAsync()));
            return;
        }

        var deviceCombo = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 6, 0, 0), ItemsSource = devices, DisplayMemberPath = "DisplayName", SelectedIndex = 0 };
        var inputCombo = NewP07Combo();
        var videoCombo = NewP07Combo();
        var audioCombo = NewP07Combo();
        var timecodeCombo = NewP07Combo();
        var tapeInput = new TextBox { MinWidth = 260, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 8, 10, 8), MaxLength = 120 };
        var stateText = NewP07State(_arabic ? "اختر الإعدادات ثم نفّذ الفحص الأولي." : "Select the profile, then run preflight.");
        var telemetryText = NewP07State(_arabic ? "لا توجد جلسة نشطة." : "No active capture session.");
        var protectionText = NewP07State(_arabic ? "لم يبدأ التسليم إلى Primary/Backup." : "Primary/Backup handoff has not started.");

        void ApplyDevice()
        {
            if (deviceCombo.SelectedItem is not CaptureDeviceDescriptor device) return;
            inputCombo.ItemsSource = device.Inputs;
            videoCombo.ItemsSource = device.VideoProfiles;
            audioCombo.ItemsSource = device.AudioProfiles;
            timecodeCombo.ItemsSource = device.TimecodeSources;
            inputCombo.SelectedIndex = device.Inputs.Count > 0 ? 0 : -1;
            videoCombo.SelectedIndex = device.VideoProfiles.Count > 0 ? 0 : -1;
            audioCombo.SelectedIndex = device.AudioProfiles.Count > 0 ? 0 : -1;
            timecodeCombo.SelectedIndex = device.TimecodeSources.Count > 0 ? 0 : -1;
        }
        deviceCombo.SelectionChanged += (_, _) => ApplyDevice();
        ApplyDevice();

        var preflightButton = P07ActionButton(_arabic ? "فحص أولي" : "Run preflight");
        var startButton = P07ActionButton(_arabic ? "بدء التسجيل" : "Start recording");
        var statusButton = P07ActionButton(_arabic ? "تحديث الحالة" : "Refresh status");
        var finalizeButton = P07ActionButton(_arabic ? "إيقاف واعتماد ورفع" : "Stop, finalize & upload");
        startButton.IsEnabled = false;
        statusButton.IsEnabled = _p07ActiveSessionId.HasValue;
        finalizeButton.IsEnabled = _p07ActiveSessionId.HasValue;

        CaptureSessionRequest? BuildRequest()
        {
            if (deviceCombo.SelectedItem is not CaptureDeviceDescriptor device) return null;
            var input = inputCombo.SelectedItem as string;
            var video = videoCombo.SelectedItem as string;
            var audio = audioCombo.SelectedItem as string;
            var timecode = timecodeCombo.SelectedItem as string;
            var tapeId = tapeInput.Text.Trim();
            var container = Environment.GetEnvironmentVariable("MAM_CAPTURE_CONTAINER");
            var codec = Environment.GetEnvironmentVariable("MAM_CAPTURE_CODEC");
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(video) ||
                string.IsNullOrWhiteSpace(audio) || string.IsNullOrWhiteSpace(timecode) ||
                string.IsNullOrWhiteSpace(tapeId) || string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(codec)) return null;
            return new CaptureSessionRequest(Environment.MachineName, device.Provider, device.DeviceId, input, video, audio,
                timecode, container, codec, tapeId, _p07CacheRoot!);
        }

        preflightButton.Click += (_, _) =>
        {
            var request = BuildRequest();
            var requiredRaw = Environment.GetEnvironmentVariable("MAM_CAPTURE_REQUIRED_CACHE_BYTES");
            var requiredOk = long.TryParse(requiredRaw, out var requiredBytes) && requiredBytes > 0;
            var root = Path.GetPathRoot(_p07CacheRoot!);
            var cacheReady = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
            var available = cacheReady ? new DriveInfo(root!).AvailableFreeSpace : 0;
            var profileSupported = request is not null;
            var apiReachable = _p03UploadClient is not null;
            var result = CapturePreflightResult.Evaluate(new(
                deviceCombo.SelectedItem is CaptureDeviceDescriptor,
                profileSupported,
                cacheReady,
                apiReachable,
                available,
                requiredOk ? requiredBytes : 0));

            var offlineApproved = string.Equals(Environment.GetEnvironmentVariable("MAM_CAPTURE_ALLOW_OFFLINE_RECOVERY"), "true", StringComparison.OrdinalIgnoreCase);
            var canRecord = result.CanRecord && (apiReachable || offlineApproved);
            startButton.IsEnabled = canRecord && !_p07ActiveSessionId.HasValue;
            var messages = result.Diagnostics.Select(x => $"{(x.Blocking ? "BLOCK" : "WARN")}: {x.Message}").ToList();
            if (!requiredOk) messages.Add("BLOCK: MAM_CAPTURE_REQUIRED_CACHE_BYTES must be explicitly configured.");
            if (!apiReachable && !offlineApproved) messages.Add("BLOCK: Central API is unavailable and offline recovery policy is not explicitly approved.");
            if (request is null) messages.Add("BLOCK: Tape ID, device profile, container and codec must be explicitly configured.");
            stateText.Text = messages.Count == 0
                ? (_arabic ? "الفحص ناجح. التسجيل متاح." : "Preflight passed. Recording is enabled.")
                : string.Join(Environment.NewLine, messages);
        };

        startButton.Click += async (_, _) =>
        {
            var request = BuildRequest();
            if (request is null || _p07CaptureProvider is null || _p07RecoveryStore is null) return;
            SetP07Busy(true, preflightButton, startButton, finalizeButton);
            try
            {
                var started = await _p07CaptureProvider.StartAsync(request, default);
                _p07ActiveRequest = request;
                _p07ActiveSessionId = started.SessionId;
                _p07LastTimecode = started.Timecode;
                await _p07RecoveryStore.SaveAsync(new(started.SessionId, request.TapeId, string.Empty,
                    started.State, 0, started.DroppedFrames, DateTimeOffset.UtcNow));
                telemetryText.Text = FormatP07Telemetry(started);
                stateText.Text = _arabic ? "التسجيل نشط. ذاكرة الالتقاط مؤقتة وقابلة للاسترداد." : "Recording active. Capture cache is temporary and recovery-backed.";
                statusButton.IsEnabled = true;
                finalizeButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                stateText.Text = (_arabic ? "فشل بدء التسجيل: " : "Capture start failed: ") + ex.Message;
            }
            finally { SetP07Busy(false, preflightButton, startButton, finalizeButton); startButton.IsEnabled = false; }
        };

        statusButton.Click += async (_, _) =>
        {
            if (_p07CaptureProvider is null || _p07ActiveSessionId is not Guid sessionId) return;
            try
            {
                var snapshot = await _p07CaptureProvider.GetStatusAsync(sessionId, default);
                _p07LastTimecode = snapshot.Timecode;
                telemetryText.Text = FormatP07Telemetry(snapshot);
                if (_p07RecoveryStore is not null && _p07ActiveRequest is not null)
                    await _p07RecoveryStore.SaveAsync(new(sessionId, _p07ActiveRequest.TapeId, string.Empty,
                        snapshot.State, 0, snapshot.DroppedFrames, DateTimeOffset.UtcNow, FailureCode: snapshot.FailureCode, FailureMessage: snapshot.FailureMessage));
            }
            catch (Exception ex) { telemetryText.Text = (_arabic ? "تعذر تحديث الحالة: " : "Status refresh failed: ") + ex.Message; }
        };

        finalizeButton.Click += async (_, _) =>
        {
            if (_p07CaptureProvider is null || _p07ActiveSessionId is not Guid sessionId || _p07ActiveRequest is null || _p07RecoveryStore is null) return;
            SetP07Busy(true, preflightButton, startButton, finalizeButton);
            statusButton.IsEnabled = false;
            var request = _p07ActiveRequest;
            try
            {
                var latest = await _p07CaptureProvider.GetStatusAsync(sessionId, default);
                _p07LastTimecode = latest.Timecode;
                var finalized = await _p07CaptureProvider.StopAndFinalizeAsync(sessionId, default);
                if (finalized.State != CaptureSessionState.ReadyForUpload || !File.Exists(finalized.TemporaryArtifactPath))
                    throw new InvalidOperationException(finalized.FailureMessage ?? "Capture did not finalize into a recoverable artifact.");

                var local = new FileInfo(finalized.TemporaryArtifactPath);
                var localSha = await ComputeFileSha256Async(local.FullName);
                if (local.Length != finalized.Length || !string.Equals(localSha, finalized.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Local finalized capture does not match provider length/SHA-256 evidence.");

                var handoff = CaptureHandoffDescriptor.From(request, finalized, _p07LastTimecode, DateTimeOffset.UtcNow);
                await _p07RecoveryStore.SaveAsync(new(sessionId, request.TapeId, handoff.TemporaryArtifactPath,
                    CaptureSessionState.ReadyForUpload, handoff.Length, handoff.DroppedFrames, DateTimeOffset.UtcNow));
                telemetryText.Text = $"ReadyForUpload · {handoff.Length:N0} bytes · SHA-256 {handoff.Sha256[..16]}… · dropped {handoff.DroppedFrames}";

                if (_p03UploadClient is null)
                {
                    protectionText.Text = _arabic ? "الخدمة المركزية غير متاحة؛ تم الاحتفاظ بالملف وبيانات الاسترداد دون حذف." : "Central API unavailable; finalized capture and recovery evidence were preserved without cleanup.";
                    return;
                }

                var uploaded = await UploadP07CaptureAsync(handoff, _p07RecoveryStore, protectionText);
                if (uploaded is null) return;

                if (uploaded.Length != handoff.Length || !string.Equals(uploaded.Sha256, handoff.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Authoritative Primary result does not match finalized capture evidence.");

                var protection = _protectionClient is null ? null : await QueueAndReadP07ProtectionAsync(uploaded.AssetId);
                if (protection?.State == BackupProtectionState.Protected)
                {
                    if (File.Exists(handoff.TemporaryArtifactPath)) File.Delete(handoff.TemporaryArtifactPath);
                    await _p07RecoveryStore.DeleteAsync(sessionId);
                    protectionText.Text = _arabic
                        ? $"تم التحقق من Primary وBackup ثم تنظيف الذاكرة المؤقتة بأمان. Asset {uploaded.AssetId:D}"
                        : $"Primary and Backup verified; temporary capture cache was safely cleaned. Asset {uploaded.AssetId:D}";
                }
                else
                {
                    protectionText.Text = _arabic
                        ? $"تم اعتماد Primary للأصل {uploaded.AssetId:D}. الحماية: {protection?.State.ToString() ?? "غير متاحة"}. تم الاحتفاظ بالملف المؤقت حتى اكتمال الحماية."
                        : $"Primary promoted for asset {uploaded.AssetId:D}. Protection: {protection?.State.ToString() ?? "unavailable"}. Temporary capture remains until protection is verified.";
                }
            }
            catch (MamApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                protectionText.Text = _arabic ? "لا توجد صلاحية للتسليم المركزي؛ تم الاحتفاظ بملف الاسترداد." : "Central handoff permission denied; recovery artifact was preserved.";
            }
            catch (Exception ex)
            {
                protectionText.Text = (_arabic ? "توقف التسليم دون حذف الملف المؤقت: " : "Handoff stopped without deleting the temporary capture: ") + ex.Message;
                try
                {
                    var prior = await _p07RecoveryStore.ReadAsync(sessionId);
                    if (prior is not null) await _p07RecoveryStore.SaveAsync(prior with { UpdatedUtc = DateTimeOffset.UtcNow, FailureCode = "capture.handoff.failed", FailureMessage = ex.Message });
                }
                catch { }
            }
            finally
            {
                _p07ActiveSessionId = null;
                _p07ActiveRequest = null;
                finalizeButton.IsEnabled = false;
                SetP07Busy(false, preflightButton, startButton, finalizeButton);
                startButton.IsEnabled = false;
            }
        };

        var profileGrid = new Grid();
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition());
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition());
        profileGrid.Children.Add(P07Field(_arabic ? "الجهاز" : "Device", deviceCombo, 0, 0));
        profileGrid.Children.Add(P07Field(_arabic ? "المدخل" : "Input", inputCombo, 1, 0));
        profileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        profileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        profileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        profileGrid.Children.Add(P07Field(_arabic ? "الفيديو" : "Video profile", videoCombo, 0, 1));
        profileGrid.Children.Add(P07Field(_arabic ? "الصوت" : "Audio profile", audioCombo, 1, 1));
        profileGrid.Children.Add(P07Field(_arabic ? "التايم كود" : "Timecode", timecodeCombo, 0, 2));
        profileGrid.Children.Add(P07Field(_arabic ? "معرف الشريط" : "Tape ID", tapeInput, 1, 2));

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(preflightButton);
        actions.Children.Add(startButton);
        actions.Children.Add(statusButton);
        actions.Children.Add(finalizeButton);

        ContentHost.Content = Scroll(PageStack(
            Lead(_arabic ? "التسجيل من الشريط" : "Windows Tape Capture",
                BuildInfo.Current.EnvironmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    ? (_arabic ? "يلزم موفر أجهزة معتمد. لا يُسمح بمحاكي CI في Production." : "An approved hardware provider is required. The CI simulator is prohibited in Production.")
                    : (_arabic ? "المحاكي متاح للتطوير فقط ولا يُعد قبول أجهزة حقيقية." : "Development simulator path only; this is never real-hardware acceptance.")),
            Card(_arabic ? "الجهاز والملف التعريفي" : "Device & profile", profileGrid),
            Card(_arabic ? "الفحص والتحكم" : "Preflight & control", new StackPanel { Children = { actions, stateText } }),
            ThreeColumn(
                Card(_arabic ? "المعاينة / الحالة" : "Preview / status", telemetryText),
                Card(_arabic ? "الصوت والتايم كود" : "Audio & timecode", new TextBlock { Text = _arabic ? "تأتي القياسات من موفر التسجيل؛ حالات unavailable/error صريحة." : "Telemetry comes from the capture provider; unavailable/error states are explicit.", Foreground = Text(), TextWrapping = TextWrapping.Wrap }),
                Card(_arabic ? "التسليم والحماية" : "Handoff & protection", protectionText)),
            await BuildP07RecoveryCardAsync()));
    }

    private async Task<UploadFinalizeResult?> UploadP07CaptureAsync(CaptureHandoffDescriptor handoff, CaptureRecoveryManifestStore recovery, TextBlock state)
    {
        if (_p03UploadClient is null) return null;
        var prior = await recovery.ReadAsync(handoff.SessionId);
        UploadSessionSnapshot session;
        if (prior?.UploadSessionId is Guid uploadSessionId)
        {
            session = await _p03UploadClient.GetSessionAsync(uploadSessionId);
        }
        else
        {
            session = await _p03UploadClient.CreateSessionAsync(new CreateUploadSessionRequest(
                $"Tape {handoff.TapeId}", Path.GetFileName(handoff.TemporaryArtifactPath), handoff.Length, handoff.Sha256));
            await recovery.SaveAsync((prior ?? new CaptureRecoveryManifest(handoff.SessionId, handoff.TapeId, handoff.TemporaryArtifactPath,
                CaptureSessionState.ReadyForUpload, handoff.Length, handoff.DroppedFrames, DateTimeOffset.UtcNow)) with
            { UploadSessionId = session.Session.SessionId, AssetId = session.Session.AssetId, UpdatedUtc = DateTimeOffset.UtcNow });
        }

        await using var source = new FileStream(handoff.TemporaryArtifactPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var offset = session.ReceivedLength;
        source.Position = offset;
        var buffer = new byte[session.Session.ChunkSizeBytes];
        while (offset < handoff.Length)
        {
            var requested = (int)Math.Min(buffer.Length, handoff.Length - offset);
            var read = 0;
            while (read < requested)
            {
                var count = await source.ReadAsync(buffer.AsMemory(read, requested - read));
                if (count == 0) break;
                read += count;
            }
            if (read == 0) throw new EndOfStreamException("Finalized capture ended before its declared length.");
            var chunk = buffer.AsMemory(0, read).ToArray();
            var chunkSha = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
            await using var chunkStream = new MemoryStream(chunk, writable: false);
            var result = await _p03UploadClient.PutChunkAsync(session.Session.SessionId, offset, chunkSha, chunkStream);
            offset = result.ReceivedLength;
            source.Position = offset;
            state.Text = $"{(_arabic ? "تسليم إلى Primary" : "Uploading to Primary")} {(int)(offset * 100d / handoff.Length)}%";
        }
        var finalized = await _p03UploadClient.FinalizeAsync(session.Session.SessionId);
        var manifest = await recovery.ReadAsync(handoff.SessionId);
        if (manifest is not null)
            await recovery.SaveAsync(manifest with { AssetId = finalized.AssetId, UpdatedUtc = DateTimeOffset.UtcNow, FailureCode = null, FailureMessage = null });
        return finalized;
    }

    private async Task<BackupProtectionRecord?> QueueAndReadP07ProtectionAsync(Guid assetId)
    {
        if (_protectionClient is null) return null;
        await _protectionClient.QueueAsync();
        return await _protectionClient.GetAssetAsync(assetId);
    }

    private async Task<FrameworkElement> BuildP07RecoveryCardAsync()
    {
        if (_p07RecoveryStore is null)
            return Card(_arabic ? "الاسترداد" : "Recovery", new TextBlock { Text = _arabic ? "مخزن الاسترداد غير مهيأ." : "Recovery store is not configured.", Foreground = Text() });

        var manifests = await _p07RecoveryStore.ListAsync();
        if (manifests.Count == 0)
            return Card(_arabic ? "الاسترداد" : "Recovery", new TextBlock { Text = _arabic ? "لا توجد جلسات معلقة." : "No pending capture recovery manifests.", Foreground = Text() });

        var stack = new StackPanel();
        foreach (var item in manifests.Take(5))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{item.TapeId} · {item.State} · {item.ObservedLength:N0} bytes · {item.UpdatedUtc:O}" +
                       (item.UploadSessionId.HasValue ? $" · upload {item.UploadSessionId.Value:D}" : string.Empty),
                Foreground = Text(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3)
            });
        }
        return Card(_arabic ? "جلسات قابلة للاسترداد" : "Recoverable sessions", stack);
    }

    private FrameworkElement BuildP07Failure(string title, string detail) => Scroll(PageStack(
        Lead(_arabic ? "التسجيل من الشريط" : "Windows Tape Capture", title),
        StateCard("Degraded", detail, "#FEF3F2", "#B42318")));

    private string FormatP07Telemetry(CaptureSessionSnapshot snapshot)
    {
        var timecode = CaptureTimecode.TryNormalize(snapshot.Timecode, out var normalized) ? normalized : (_arabic ? "غير متاح" : "unavailable");
        var audio = snapshot.AudioPeaksDb.Count == 0 ? (_arabic ? "غير متاح" : "unavailable") : string.Join(" / ", snapshot.AudioPeaksDb.Select((x, i) => $"CH{i + 1} {x:0.0} dB"));
        return $"{snapshot.State} · {snapshot.RecordedDuration:hh\:mm\:ss}\nTIMECODE {timecode}\n{audio}\nDropped frames: {snapshot.DroppedFrames}";
    }

    private static ComboBox NewP07Combo() => new() { MinWidth = 260, Margin = new Thickness(0, 6, 0, 0) };

    private static TextBlock NewP07State(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 0),
        Foreground = Text(),
        TextWrapping = TextWrapping.Wrap
    };

    private static Button P07ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 0, 10, 6),
        Padding = new Thickness(14, 9, 14, 9),
        Background = Gold(),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static Border P07Field(string label, Control control, int column, int row)
    {
        var stack = new StackPanel { Margin = new Thickness(6) };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = Text() });
        stack.Children.Add(control);
        var border = new Border { Child = stack };
        Grid.SetColumn(border, column);
        Grid.SetRow(border, row);
        return border;
    }

    private static void SetP07Busy(bool busy, params Button[] buttons)
    {
        foreach (var button in buttons) button.IsEnabled = !busy;
    }
}
