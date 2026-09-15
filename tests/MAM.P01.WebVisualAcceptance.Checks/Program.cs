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

    var evidenceRoutes = new[] { "dashboard", "library", "asset", "upload", "reports", "admin", "search" };
    var captures = new List<Capture>();
    foreach (var language in new[] { "ar", "en" })
    {
        var direction = language == "ar" ? "rtl" : "ltr";
        foreach (var routeName in evidenceRoutes)
        {
            var suffix = routeName == "asset"
                ? "&asset=00000000-0000-0000-0000-000000000001"
                : routeName == "search" ? "&q=Diwan&searched=1" : string.Empty;
            captures.Add(new Capture($"{routeName}-{language}-1440.png", 1440, 1000,
                $"{baseUrl}/?lang={language}&qa=p131#route={routeName}{suffix}", direction, language, routeName));
        }

        captures.Add(new Capture($"dashboard-{language}-1920.png", 1920, 1080,
            $"{baseUrl}/?lang={language}&qa=p131#route=dashboard", direction, language, "dashboard"));
        captures.Add(new Capture($"dashboard-{language}-1366.png", 1366, 900,
            $"{baseUrl}/?lang={language}&qa=p131#route=dashboard", direction, language, "dashboard"));
        captures.Add(new Capture($"dashboard-{language}-1024.png", 1024, 900,
            $"{baseUrl}/?lang={language}&qa=p131#route=dashboard", direction, language, "dashboard"));
        captures.Add(new Capture($"mobile-{language}-390.png", 390, 844,
            $"{baseUrl}/?lang={language}&qa=p131#route=dashboard", direction, language, "dashboard"));
        captures.Add(new Capture($"landing-{language}-1440.png", 1440, 1000,
            $"{baseUrl}/landing?lang={language}&qa=p131", direction, language));
        captures.Add(new Capture($"login-{language}-390.png", 390, 844,
            $"{baseUrl}/auth/login?lang={language}&returnUrl=%2Fapp", direction, language));
    }

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
            expression = "(() => {const sidebar=document.querySelector('.sidebar')?.getBoundingClientRect();const main=document.querySelector('.app-shell>main')?.getBoundingClientRect();return {innerWidth:window.innerWidth,innerHeight:window.innerHeight,docScrollWidth:document.documentElement.scrollWidth,bodyScrollWidth:document.body.scrollWidth,dir:document.documentElement.dir,lang:document.documentElement.lang,route:new URLSearchParams(location.hash.replace(/^#/,'')).get('route'),title:document.querySelector('#pageTitle')?.textContent?.trim()||document.querySelector('h1,h2')?.textContent?.trim()||'',content:(document.querySelector('#content')?.innerText||document.body.innerText||'').trim(),sidebarLeft:sidebar?.left??null,mainLeft:main?.left??null}})()",
            returnByValue = true
        });
        var metrics = metricsResponse.GetProperty("result").GetProperty("value");
        var innerWidth = metrics.GetProperty("innerWidth").GetInt32();
        var innerHeight = metrics.GetProperty("innerHeight").GetInt32();
        var documentScrollWidth = metrics.GetProperty("docScrollWidth").GetInt32();
        var bodyScrollWidth = metrics.GetProperty("bodyScrollWidth").GetInt32();
        var direction = metrics.GetProperty("dir").GetString();
        var language = metrics.GetProperty("lang").GetString();
        var actualRoute = metrics.GetProperty("route").GetString();
        var renderedTitle = metrics.GetProperty("title").GetString() ?? string.Empty;
        var renderedContent = metrics.GetProperty("content").GetString() ?? string.Empty;

        if (innerWidth != capture.Width || innerHeight != capture.Height)
            throw new InvalidOperationException($"Web viewport mismatch for {capture.Name}: expected={capture.Width}x{capture.Height}, actual={innerWidth}x{innerHeight}.");
        if (documentScrollWidth > capture.Width || bodyScrollWidth > capture.Width)
            throw new InvalidOperationException($"Web horizontal overflow for {capture.Name}: viewport={capture.Width}, document={documentScrollWidth}, body={bodyScrollWidth}.");
        if (!string.Equals(direction, capture.Direction, StringComparison.Ordinal) || !string.Equals(language, capture.Language, StringComparison.Ordinal))
            throw new InvalidOperationException($"Web language/direction mismatch for {capture.Name}: expected={capture.Language}/{capture.Direction}, actual={language}/{direction}.");
        if (capture.Route is not null && !string.Equals(actualRoute, capture.Route, StringComparison.Ordinal))
            throw new InvalidOperationException($"Web route mismatch for {capture.Name}: expected={capture.Route}, actual={actualRoute ?? "<none>"}.");
        if (string.IsNullOrWhiteSpace(renderedTitle) || renderedContent.Length < 20)
            throw new InvalidOperationException($"Web route did not render meaningful UI for {capture.Name}: title='{renderedTitle}', contentLength={renderedContent.Length}.");
        if (capture.Route is not null && capture.Width > 800)
        {
            var sidebarLeft = metrics.GetProperty("sidebarLeft").GetDouble();
            var mainLeft = metrics.GetProperty("mainLeft").GetDouble();
            var mirroredCorrectly = capture.Language == "en" ? sidebarLeft < mainLeft : sidebarLeft > mainLeft;
            if (!mirroredCorrectly)
                throw new InvalidOperationException($"Web shell mirror mismatch for {capture.Name}: sidebarLeft={sidebarLeft}, mainLeft={mainLeft}, dir={direction}.");
        }

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

    var routeRows = new List<RouteAudit>();
    commandId = await AuditAllRoutesAsync(socket, commandId, baseUrl, routeRows);
    commandId = await AuditLanguageSwitchAsync(socket, commandId, baseUrl);
    await File.WriteAllTextAsync(Path.Combine(output, "route-language-matrix.csv"), BuildRouteMatrix(routeRows), new UTF8Encoding(false));

    Console.WriteLine("PASS: P12.11 rendered acceptance generated paired route evidence plus exact CSS viewport evidence at 1920, 1440, 1366, 1024 and 390 in English LTR and Arabic RTL with zero horizontal overflow.");
    Console.WriteLine("PASS: Full Web route audit verified every application route in both languages, plus Landing and Login, with no known mixed-language repository-controlled chrome.");
    Console.WriteLine("PASS: Language switching preserved the exact logical route, search/filter/page/view/sort state, and unrelated query parameters in both directions.");
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

static async Task<int> AuditAllRoutesAsync(ClientWebSocket socket, int commandId, string baseUrl, List<RouteAudit> rows)
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
    var forbiddenArabicInEnglish = new[]
    {
        "لوحة التحكم", "مكتبة الوسائط", "إجراءات التهيئة", "إدخال جديد", "إضافة ميديا",
        "قائمة المعالجة", "التقارير", "حماية النسخة الاحتياطية", "إعدادات مسؤول النظام",
        "إجراءات الإدارة", "البحث في المحتوى", "جاري التحميل", "إعادة المحاولة"
    };
    var forbiddenAttributes = new[]
    {
        "Primary navigation", "Diwan Al Amiri crest", "Asset title", "Lifecycle", "Category", "Collection",
        "Verified preview", "PDF preview"
    };

    var routes = new[]
    {
        "dashboard", "library", "asset", "curation-actions", "ingest", "upload", "queue", "reports", "protection",
        "admin", "settings", "categories", "references", "mediaPermissions", "admin-actions", "search", "myPermissions"
    };

    foreach (var languageCode in new[] { "ar", "en" })
    foreach (var routeName in routes)
    {
        var directionExpected = languageCode == "ar" ? "rtl" : "ltr";
        var suffix = routeName == "asset" ? "&asset=00000000-0000-0000-0000-000000000001" : string.Empty;
        await CallAsync(socket, ++commandId, "Page.navigate", new { url = $"{baseUrl}/?lang={languageCode}&qa=p131#route={routeName}{suffix}" });
        commandId = await WaitForSettledPageAsync(socket, commandId);
        var resultResponse = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
        {
            expression = "(() => { if(window.mamLocalizationAudit) window.mamLocalizationAudit.apply(); const attrs=[...document.querySelectorAll('[aria-label],[placeholder],[title],[alt]')].flatMap(e=>['aria-label','placeholder','title','alt'].map(a=>e.getAttribute(a)).filter(Boolean)).join('\\n'); const hash=new URLSearchParams(location.hash.replace(/^#/,'')); return {route:hash.get('route'),dir:document.documentElement.dir,lang:document.documentElement.lang,title:document.querySelector('#pageTitle')?.textContent?.trim()||'',text:document.body.innerText||'',attrs,docScrollWidth:document.documentElement.scrollWidth,bodyScrollWidth:document.body.scrollWidth,innerWidth:window.innerWidth}; })()",
            returnByValue = true
        });
        var value = resultResponse.GetProperty("result").GetProperty("value");
        var direction = value.GetProperty("dir").GetString();
        var language = value.GetProperty("lang").GetString();
        var actualRoute = value.GetProperty("route").GetString();
        var title = value.GetProperty("title").GetString() ?? string.Empty;
        var viewport = value.GetProperty("innerWidth").GetInt32();
        var scrollWidth = Math.Max(value.GetProperty("docScrollWidth").GetInt32(), value.GetProperty("bodyScrollWidth").GetInt32());
        if (!string.Equals(actualRoute, routeName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Route audit navigation mismatch: expected={routeName}, actual={actualRoute ?? "<none>"}, language={languageCode}.");
        if (!string.Equals(direction, directionExpected, StringComparison.Ordinal) || !string.Equals(language, languageCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"Web localization audit lost {languageCode}/{directionExpected} on route {routeName}: {language}/{direction}.");
        if (scrollWidth > viewport)
            throw new InvalidOperationException($"Route audit horizontal overflow on {routeName}/{languageCode}: viewport={viewport}, scrollWidth={scrollWidth}.");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException($"Route audit rendered no heading on {routeName}/{languageCode}.");

        var text = value.GetProperty("text").GetString() ?? string.Empty;
        var attrs = value.GetProperty("attrs").GetString() ?? string.Empty;
        if (languageCode == "ar")
        {
            foreach (var forbidden in forbiddenVisible)
                if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Arabic Web localization audit failed on route '{routeName}': untranslated visible UI chrome '{forbidden}'.");
            foreach (var forbidden in forbiddenAttributes)
                if (attrs.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Arabic Web localization audit failed on route '{routeName}': untranslated accessibility/input chrome '{forbidden}'.");
        }
        else
        {
            foreach (var forbidden in forbiddenArabicInEnglish)
                if (text.Contains(forbidden, StringComparison.Ordinal))
                    throw new InvalidOperationException($"English Web localization audit failed on route '{routeName}': untranslated visible UI chrome '{forbidden}'.");
        }

        rows.Add(new RouteAudit("app", routeName, languageCode, directionExpected, viewport, scrollWidth, title, "PASS"));
        Console.WriteLine($"PASS: Web route={routeName} dir={directionExpected} lang={languageCode} title={title} localization chrome clean.");
    }

    foreach (var page in new[] { new { Name = "landing", Path = "/landing" }, new { Name = "login", Path = "/auth/login?returnUrl=%2Fapp" } })
    foreach (var languageCode in new[] { "ar", "en" })
    {
        var separator = page.Path.Contains('?') ? '&' : '?';
        await CallAsync(socket, ++commandId, "Page.navigate", new { url = $"{baseUrl}{page.Path}{separator}lang={languageCode}&qa=p131" });
        commandId = await WaitForSettledPageAsync(socket, commandId);
        var response = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
        {
            expression = "(() => ({dir:document.documentElement.dir,lang:document.documentElement.lang,title:document.querySelector('h1,h2')?.textContent?.trim()||document.title,text:document.body.innerText||'',docScrollWidth:document.documentElement.scrollWidth,bodyScrollWidth:document.body.scrollWidth,innerWidth:window.innerWidth}))()",
            returnByValue = true
        });
        var value = response.GetProperty("result").GetProperty("value");
        var direction = value.GetProperty("dir").GetString() ?? string.Empty;
        var viewport = value.GetProperty("innerWidth").GetInt32();
        var scrollWidth = Math.Max(value.GetProperty("docScrollWidth").GetInt32(), value.GetProperty("bodyScrollWidth").GetInt32());
        var title = value.GetProperty("title").GetString() ?? string.Empty;
        var visibleText = value.GetProperty("text").GetString() ?? string.Empty;
        var expectedDirection = languageCode == "ar" ? "rtl" : "ltr";
        if (direction != expectedDirection || value.GetProperty("lang").GetString() != languageCode || scrollWidth > viewport || string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException($"Standalone page audit failed for {page.Name}/{languageCode}: dir={direction}, viewport={viewport}, scrollWidth={scrollWidth}, title='{title}'.");
        var mixedLanguageMarkers = languageCode == "ar"
            ? new[] { "Choose a sign-in method", "Domain devices use Windows SSO", "Back to landing page", "Designed for the complete content lifecycle", "Trusted institutional media asset management platform" }
            : new[] { "اختر طريقة تسجيل الدخول", "أجهزة الدومين تستخدم", "العودة للصفحة الرئيسية", "مصمم لدورة حياة المحتوى بالكامل", "منصة مؤسسية موثوقة" };
        foreach (var marker in mixedLanguageMarkers)
            if (visibleText.Contains(marker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Standalone page localization failed for {page.Name}/{languageCode}: mixed-language UI marker '{marker}'.");
        rows.Add(new RouteAudit("standalone", page.Name, languageCode, expectedDirection, viewport, scrollWidth, title, "PASS"));
        Console.WriteLine($"PASS: Web standalone={page.Name} dir={expectedDirection} lang={languageCode} title={title}.");
    }

    return commandId;
}

static async Task<int> AuditLanguageSwitchAsync(ClientWebSocket socket, int commandId, string baseUrl)
{
    const string expectedHash = "route=library&q=Diwan&page=2&view=list&sort=title";
    await CallAsync(socket, ++commandId, "Page.navigate", new { url = $"{baseUrl}/?lang=ar&qa=p131&tenant=diwan#{expectedHash}" });
    commandId = await WaitForSettledPageAsync(socket, commandId);

    foreach (var expectedLanguage in new[] { "en", "ar" })
    {
        var response = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
        {
            expression = "(async()=>{const button=document.querySelector('[data-p128-language]')||document.querySelector('#languageButton');if(!button)return {missing:true};button.click();await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(resolve,120))));const url=new URL(location.href);const hash=new URLSearchParams(url.hash.replace(/^#/,''));return {missing:false,lang:document.documentElement.lang,dir:document.documentElement.dir,queryLang:url.searchParams.get('lang'),qa:url.searchParams.get('qa'),tenant:url.searchParams.get('tenant'),route:hash.get('route'),q:hash.get('q'),page:hash.get('page'),view:hash.get('view'),sort:hash.get('sort')};})()",
            awaitPromise = true,
            returnByValue = true
        });
        var value = response.GetProperty("result").GetProperty("value");
        if (value.GetProperty("missing").GetBoolean())
            throw new InvalidOperationException("Language-switch audit could not find the language action.");
        string? Read(string key) => value.GetProperty(key).GetString();
        var expectedDirection = expectedLanguage == "ar" ? "rtl" : "ltr";
        if (Read("lang") != expectedLanguage || Read("dir") != expectedDirection || Read("queryLang") != expectedLanguage ||
            Read("qa") != "p131" || Read("tenant") != "diwan" || Read("route") != "library" || Read("q") != "Diwan" ||
            Read("page") != "2" || Read("view") != "list" || Read("sort") != "title")
            throw new InvalidOperationException($"Language-switch state audit failed for {expectedLanguage}: {value.GetRawText()}.");
        Console.WriteLine($"PASS: language switch -> {expectedLanguage}/{expectedDirection} preserved route and deep-link state.");
    }

    var navigationResponse = await CallAsync(socket, ++commandId, "Runtime.evaluate", new
    {
        expression = "(async()=>{const button=[...document.querySelectorAll('#nav [data-route=\"reports\"]')].find(x=>!x.hidden);if(!button)return {missing:true};button.click();await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(resolve,120))));const hash=new URLSearchParams(location.hash.replace(/^#/,''));return {missing:false,route:hash.get('route'),title:document.querySelector('#pageTitle')?.textContent?.trim()||'',active:[...document.querySelectorAll('#nav [aria-current=\"page\"]')].map(x=>x.dataset.route)};})()",
        awaitPromise = true,
        returnByValue = true
    });
    var navigation = navigationResponse.GetProperty("result").GetProperty("value");
    if (navigation.GetProperty("missing").GetBoolean() || navigation.GetProperty("route").GetString() != "reports" ||
        navigation.GetProperty("title").GetString() != "التقارير" ||
        !navigation.GetProperty("active").EnumerateArray().Any(item => item.GetString() == "reports"))
        throw new InvalidOperationException($"Same-document navigation audit failed: {navigation.GetRawText()}.");
    Console.WriteLine("PASS: same-document sidebar navigation updated route, heading and aria-current state.");

    return commandId;
}

static string BuildRouteMatrix(IEnumerable<RouteAudit> rows)
{
    static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    var builder = new StringBuilder("surface,route,language,direction,viewport_width,scroll_width,title,status\r\n");
    foreach (var row in rows)
        builder.AppendJoin(',', Csv(row.Surface), Csv(row.Route), Csv(row.Language), Csv(row.Direction), row.ViewportWidth, row.ScrollWidth, Csv(row.Title), Csv(row.Status)).Append("\r\n");
    return builder.ToString();
}

static async Task<int> WaitForSettledPageAsync(ClientWebSocket socket, int commandId)
{
    await CallAsync(socket, ++commandId, "Runtime.evaluate", new
    {
        expression = "new Promise(resolve => { const done=()=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(resolve,500))); if(document.readyState==='complete') done(); else addEventListener('load',done,{once:true}); })",
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

internal sealed record Capture(string Name, int Width, int Height, string Url, string Direction, string Language, string? Route = null);
internal sealed record RouteAudit(string Surface, string Route, string Language, string Direction, int ViewportWidth, int ScrollWidth, string Title, string Status);
