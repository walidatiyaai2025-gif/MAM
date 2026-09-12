using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/p01-visual");
var baseUrl = args.Length > 1 ? args[1].TrimEnd('/') : "http://127.0.0.1:5080";
Directory.CreateDirectory(output);

var browserPath = FindBrowser();
var profile = Path.Combine(Path.GetTempPath(), $"mam-p01-cdp-{Guid.NewGuid():N}");
Directory.CreateDirectory(profile);

Process? browser = null;
var browserDiagnostics = new StringBuilder();
var diagnosticsGate = new object();
try
{
    browser = Process.Start(new ProcessStartInfo
    {
        FileName = browserPath,
        Arguments = $"--headless --disable-gpu --no-sandbox --no-first-run --no-default-browser-check --disable-background-networking --remote-debugging-port=0 --user-data-dir=\"{profile}\" about:blank",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }) ?? throw new InvalidOperationException("Could not start Chromium for P01 Web visual acceptance.");

    browser.OutputDataReceived += (_, eventArgs) => AppendDiagnostic("stdout", eventArgs.Data);
    browser.ErrorDataReceived += (_, eventArgs) => AppendDiagnostic("stderr", eventArgs.Data);
    browser.BeginOutputReadLine();
    browser.BeginErrorReadLine();

    var port = await WaitForDevToolsPortAsync(profile, browser, browserDiagnostics, diagnosticsGate);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    using var targetResponse = await http.PutAsync($"http://127.0.0.1:{port}/json/new?about:blank", content: null);
    targetResponse.EnsureSuccessStatusCode();
    using var targetDocument = JsonDocument.Parse(await targetResponse.Content.ReadAsStringAsync());
    var webSocketUrl = targetDocument.RootElement.GetProperty("webSocketDebuggerUrl").GetString()
        ?? throw new InvalidOperationException("Chromium did not expose a page WebSocket URL.");

    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri(webSocketUrl), CancellationToken.None);
    var commandId = 0;

    await CallAsync(socket, ++commandId, "Page.enable");
    await CallAsync(socket, ++commandId, "Runtime.enable");

    var captures = new[]
    {
        new Capture("web-360-en.png", 360, 900, $"{baseUrl}/", "ltr", "en"),
        new Capture("web-360-ar.png", 360, 900, $"{baseUrl}/?lang=ar", "rtl", "ar"),
        new Capture("web-820-en.png", 820, 1000, $"{baseUrl}/", "ltr", "en"),
        new Capture("web-820-ar.png", 820, 1000, $"{baseUrl}/?lang=ar", "rtl", "ar"),
        new Capture("web-1440-en.png", 1440, 1000, $"{baseUrl}/", "ltr", "en"),
        new Capture("web-1440-ar.png", 1440, 1000, $"{baseUrl}/?lang=ar", "rtl", "ar")
    };

    foreach (var capture in captures)
    {
        await CallAsync(socket, ++commandId, "Emulation.setDeviceMetricsOverride", new
        {
            width = capture.Width,
            height = capture.Height,
            deviceScaleFactor = 1,
            mobile = false,
            screenWidth = capture.Width,
            screenHeight = capture.Height
        });

        await CallAsync(socket, ++commandId, "Page.navigate", new { url = capture.Url });
        commandId = await WaitForSettledPageAsync(socket, commandId);

        var metricsResponse = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
        {
            expression = "(() => ({innerWidth:window.innerWidth,innerHeight:window.innerHeight,docScrollWidth:document.documentElement.scrollWidth,bodyScrollWidth:document.body.scrollWidth,dir:document.documentElement.dir,lang:document.documentElement.lang}))()",
            returnByValue = true
        });
        var metrics = metricsResponse.GetProperty("result").GetProperty("value");
        var innerWidth = metrics.GetProperty("innerWidth").GetInt32();
        var innerHeight = metrics.GetProperty("innerHeight").GetInt32();
        var documentScrollWidth = metrics.GetProperty("docScrollWidth").GetInt32();
        var bodyScrollWidth = metrics.GetProperty("bodyScrollWidth").GetInt32();
        var direction = metrics.GetProperty("dir").GetString();
        var language = metrics.GetProperty("lang").GetString();

        if (innerWidth != capture.Width || innerHeight != capture.Height)
            throw new InvalidOperationException($"Web viewport mismatch for {capture.Name}: expected={capture.Width}x{capture.Height}, actual={innerWidth}x{innerHeight}.");
        if (documentScrollWidth > capture.Width || bodyScrollWidth > capture.Width)
            throw new InvalidOperationException($"Web horizontal overflow for {capture.Name}: viewport={capture.Width}, document={documentScrollWidth}, body={bodyScrollWidth}.");
        if (!string.Equals(direction, capture.Direction, StringComparison.Ordinal) || !string.Equals(language, capture.Language, StringComparison.Ordinal))
            throw new InvalidOperationException($"Web language/direction mismatch for {capture.Name}: expected={capture.Language}/{capture.Direction}, actual={language}/{direction}.");

        var screenshotResponse = await CallAsync(socket, ++commandId, "Page.captureScreenshot", new
        {
            format = "png",
            fromSurface = true,
            captureBeyondViewport = false
        });
        var encoded = screenshotResponse.GetProperty("data").GetString()
            ?? throw new InvalidOperationException($"Chromium returned no screenshot data for {capture.Name}.");
        var bytes = Convert.FromBase64String(encoded);
        ValidatePngDimensions(bytes, capture.Width, capture.Height, capture.Name);
        if (bytes.Length < 10_000)
            throw new InvalidOperationException($"Rendered Web evidence is trivial for {capture.Name}: {bytes.Length} bytes.");

        await File.WriteAllBytesAsync(Path.Combine(output, capture.Name), bytes);
        Console.WriteLine($"PASS: {capture.Name} cssViewport={innerWidth}x{innerHeight} scrollWidth={Math.Max(documentScrollWidth, bodyScrollWidth)} dir={direction} lang={language} bytes={bytes.Length}");
    }

    commandId = await AuditArabicRoutesAsync(socket, commandId, baseUrl);

    Console.WriteLine("PASS: P01 Web rendered acceptance generated exact CSS viewport evidence at 360, 820 and 1440 in English LTR and Arabic RTL with zero horizontal overflow.");
    Console.WriteLine("PASS: Arabic Web route audit found no known untranslated repository-controlled UI/accessibility chrome across Dashboard, Library, Asset, Ingest, Upload, Queue, Reports, Admin and Settings.");
    return 0;

    void AppendDiagnostic(string stream, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (diagnosticsGate)
        {
            if (browserDiagnostics.Length < 16_000)
                browserDiagnostics.Append('[').Append(stream).Append("] ").AppendLine(line);
        }
    }
}
finally
{
    if (browser is not null)
    {
        try
        {
            if (!browser.HasExited) browser.Kill(entireProcessTree: true);
        }
        catch { }
        browser.Dispose();
    }

    try { Directory.Delete(profile, recursive: true); } catch { }
}

static async Task<int> AuditArabicRoutesAsync(ClientWebSocket socket, int commandId, string baseUrl)
{
    await CallAsync(socket, ++commandId, "Emulation.setDeviceMetricsOverride", new
    {
        width = 1440,
        height = 1000,
        deviceScaleFactor = 1,
        mobile = false,
        screenWidth = 1440,
        screenHeight = 1000
    });
    await CallAsync(socket, ++commandId, "Page.navigate", new { url = $"{baseUrl}/?lang=ar" });
    commandId = await WaitForSettledPageAsync(socket, commandId);

    var forbiddenVisible = new[]
    {
        "Loading", "Empty", "API error", "Permission denied", "Degraded",
        "Demo assets", "Demo protected", "Primary + Backup verified", "DEMO QUEUE",
        "Video preview shell", "Title · event date · category · tags · preservation notes",
        "Windows-only capability", "Temporary local selection before central upload",
        "File selection area", "Drop files here or browse", "Preflight",
        "Proxy generation", "Technical metadata", "Backup verification",
        "Users & roles", "Administrator actions", "Search & facets", "Collections & policy",
        "Backup Protection", "Enterprise Administration & Policy", "Reports, Monitoring, Resilience & DR",
        "Dependency health", "Diagnostics bundle", "Language & appearance", "Central services"
    };
    var forbiddenAttributes = new[]
    {
        "Primary navigation", "Diwan Al Amiri crest", "Asset title", "Lifecycle", "Category", "Collection",
        "Verified preview", "PDF preview"
    };

    foreach (var routeName in new[] { "dashboard", "library", "asset", "ingest", "upload", "queue", "reports", "admin", "settings" })
    {
        var expression = $$"""
            (async () => {
              const button = document.querySelector(`[data-route='{{routeName}}']`);
              if (!button) return { skipped: true, dir: document.documentElement.dir, lang: document.documentElement.lang, text: '', attrs: '' };
              button.click();
              await new Promise(resolve => setTimeout(resolve, 220));
              if (window.mamLocalizationAudit) window.mamLocalizationAudit.apply();
              await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
              const attrs = [...document.querySelectorAll('[aria-label],[placeholder],[title],[alt]')]
                .flatMap(e => ['aria-label','placeholder','title','alt'].map(a => e.getAttribute(a)).filter(Boolean)).join('\n');
              return { skipped: false, dir: document.documentElement.dir, lang: document.documentElement.lang, text: document.body.innerText, attrs };
            })()
            """;
        var resultResponse = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });
        var value = resultResponse.GetProperty("result").GetProperty("value");
        if (value.GetProperty("skipped").GetBoolean())
            throw new InvalidOperationException($"Arabic Web localization audit could not find route button: {routeName}.");
        var direction = value.GetProperty("dir").GetString();
        var language = value.GetProperty("lang").GetString();
        if (!string.Equals(direction, "rtl", StringComparison.Ordinal) || !string.Equals(language, "ar", StringComparison.Ordinal))
            throw new InvalidOperationException($"Arabic Web localization audit lost ar/rtl on route {routeName}: {language}/{direction}.");

        var text = value.GetProperty("text").GetString() ?? string.Empty;
        var attrs = value.GetProperty("attrs").GetString() ?? string.Empty;
        foreach (var forbidden in forbiddenVisible)
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Arabic Web localization audit failed on route '{routeName}': untranslated visible UI chrome '{forbidden}'.");
        foreach (var forbidden in forbiddenAttributes)
            if (attrs.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Arabic Web localization audit failed on route '{routeName}': untranslated accessibility/input chrome '{forbidden}'.");

        Console.WriteLine($"PASS: Arabic Web route={routeName} dir=rtl lang=ar localization chrome clean.");
    }

    return commandId;
}

static async Task<int> WaitForSettledPageAsync(ClientWebSocket socket, int commandId)
{
    await CallAsync(socket, ++commandId, "Runtime.evaluate", new
    {
        expression = "new Promise(resolve => { const done=()=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(resolve,250))); if(document.readyState==='complete') done(); else addEventListener('load',done,{once:true}); })",
        awaitPromise = true,
        returnByValue = true
    });
    return commandId;
}

static string FindBrowser()
{
    var candidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
    };

    return candidates.FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException("No supported Chromium browser was found on the Windows runner.");
}

static async Task<int> WaitForDevToolsPortAsync(string profile, Process browser, StringBuilder diagnostics, object diagnosticsGate)
{
    var path = Path.Combine(profile, "DevToolsActivePort");
    var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(45);

    while (DateTimeOffset.UtcNow < timeoutAt)
    {
        if (File.Exists(path))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(path);
                if (lines.Length > 0 && int.TryParse(lines[0], out var port) && port > 0)
                    return port;
            }
            catch (IOException)
            {
                // Chromium may still be writing the port file. Retry until the bounded deadline.
            }
        }

        if (browser.HasExited)
            throw new InvalidOperationException($"Chromium exited before DevTools became available. exitCode={browser.ExitCode}. Diagnostics:{Environment.NewLine}{SnapshotDiagnostics(diagnostics, diagnosticsGate)}");

        await Task.Delay(100);
    }

    throw new InvalidOperationException($"Chromium DevTools port did not become available within 45 seconds. browser={browser.StartInfo.FileName}. Diagnostics:{Environment.NewLine}{SnapshotDiagnostics(diagnostics, diagnosticsGate)}");
}

static string SnapshotDiagnostics(StringBuilder diagnostics, object diagnosticsGate)
{
    lock (diagnosticsGate)
        return diagnostics.Length == 0 ? "<no browser output>" : diagnostics.ToString();
}

static async Task<JsonElement> CallAsync(ClientWebSocket socket, int id, string method, object? parameters = null)
{
    var message = JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters ?? new { }
    });
    var payload = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    while (true)
    {
        var json = await ReceiveMessageAsync(socket);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"CDP {method} failed: {error.GetRawText()}");
        return root.TryGetProperty("result", out var result) ? result.Clone() : default;
    }
}

static async Task<string> ReceiveMessageAsync(ClientWebSocket socket)
{
    using var stream = new MemoryStream();
    var buffer = new byte[16 * 1024];
    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new InvalidOperationException("Chromium DevTools WebSocket closed unexpectedly.");
        stream.Write(buffer, 0, result.Count);
        if (result.EndOfMessage) break;
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}

static void ValidatePngDimensions(byte[] png, int expectedWidth, int expectedHeight, string name)
{
    if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
        throw new InvalidOperationException($"Rendered Web evidence is not a PNG: {name}.");

    var width = ReadBigEndianInt32(png, 16);
    var height = ReadBigEndianInt32(png, 20);
    if (width != expectedWidth || height != expectedHeight)
        throw new InvalidOperationException($"Rendered Web PNG size mismatch for {name}: expected={expectedWidth}x{expectedHeight}, actual={width}x{height}.");
}

static int ReadBigEndianInt32(byte[] bytes, int offset) =>
    (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

internal sealed record Capture(string Name, int Width, int Height, string Url, string Direction, string Language);
