using System.Text.Json;
using System.Text.RegularExpressions;

namespace MAM.Application.Diagnostics;

public sealed record RuntimeDiagnosticEvent(
    string Level,
    string Kind,
    string? Message = null,
    string? ExceptionType = null,
    string? Stack = null,
    string? CorrelationId = null,
    string? Route = null,
    string? Method = null,
    int? Status = null,
    string? User = null,
    IReadOnlyDictionary<string, string?>? Metadata = null);

public sealed class RuntimeInspectorLog
{
    public const int RetentionDays = 30;
    private const long MaxSegmentBytes = 20L * 1024L * 1024L;
    private const int MaxSupportExportBytes = 12 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _component;
    private readonly string _root;
    private readonly BuildInfo _build;

    public RuntimeInspectorLog(string component, BuildInfo? build = null, string? rootPath = null)
    {
        _component = Clean(component, 80) ?? "MAM";
        _build = build ?? BuildInfo.Current;
        _root = ResolveWritableRoot(rootPath);
        CleanupOldSegments();
    }

    public string RootPath => _root;

    public string? LatestFile
    {
        get
        {
            try
            {
                return Directory.EnumerateFiles(_root, "mam-runtime-*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }

    public byte[] CreateSupportExport(TimeSpan? lookback = null)
    {
        var since = DateTime.UtcNow - (lookback ?? TimeSpan.FromDays(3));
        var files = Directory.EnumerateFiles(_root, "mam-runtime-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc >= since)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var buffer = new MemoryStream();
        using var writer = new StreamWriter(buffer, new System.Text.UTF8Encoding(false), 4096, leaveOpen: true);
        writer.WriteLine("# Diwan MAM Runtime Inspector support export");
        writer.WriteLine($"# generatedUtc={DateTimeOffset.UtcNow:O}");
        writer.WriteLine($"# component={_component}");
        writer.WriteLine($"# version={_build.Version}");
        writer.WriteLine($"# commitSha={_build.CommitSha}");
        writer.WriteLine($"# lookbackHours={(lookback ?? TimeSpan.FromDays(3)).TotalHours:0}");
        writer.WriteLine($"# retentionDays={RetentionDays}");
        writer.WriteLine();

        var remaining = MaxSupportExportBytes;
        foreach (var file in files)
        {
            if (remaining <= 0) break;
            writer.WriteLine($"# --- {file.Name} ---");
            writer.Flush();

            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > remaining)
                stream.Seek(Math.Max(0, stream.Length - remaining), SeekOrigin.Begin);

            var copyLength = Math.Min(remaining, (int)Math.Min(int.MaxValue, stream.Length - stream.Position));
            var chunk = new byte[Math.Min(81920, Math.Max(1, copyLength))];
            while (copyLength > 0)
            {
                var read = stream.Read(chunk, 0, Math.Min(chunk.Length, copyLength));
                if (read <= 0) break;
                buffer.Write(chunk, 0, read);
                copyLength -= read;
                remaining -= read;
            }
            writer.WriteLine();
            writer.Flush();
        }

        if (remaining <= 0)
        {
            writer.WriteLine();
            writer.WriteLine("# Export truncated at 12 MB. Upload the individual component logs if older context is required.");
        }

        writer.Flush();
        return buffer.ToArray();
    }

    public void Write(RuntimeDiagnosticEvent entry)
    {
        try
        {
            var safe = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                component = _component,
                level = Clean(entry.Level, 20) ?? "Error",
                kind = Clean(entry.Kind, 120) ?? "runtime",
                correlationId = Clean(entry.CorrelationId, 120),
                message = Clean(entry.Message, 8000),
                exceptionType = Clean(entry.ExceptionType, 300),
                stack = Clean(entry.Stack, 24000),
                route = Clean(entry.Route, 1000),
                method = Clean(entry.Method, 20),
                status = entry.Status,
                user = Clean(entry.User, 300),
                machine = Environment.MachineName,
                processId = Environment.ProcessId,
                version = _build.Version,
                commitSha = _build.CommitSha,
                buildNumber = _build.BuildNumber,
                environment = _build.EnvironmentName,
                metadata = SanitizeMetadata(entry.Metadata)
            };

            var line = JsonSerializer.Serialize(safe) + Environment.NewLine;
            lock (_gate)
            {
                var path = CurrentSegmentPath();
                File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never break the application.
        }
    }

    private string CurrentSegmentPath()
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = $"mam-runtime-{FileComponent(_component)}-{day}";
        var candidate = Path.Combine(_root, prefix + ".log");
        if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaxSegmentBytes) return candidate;

        for (var part = 2; part < 1000; part++)
        {
            candidate = Path.Combine(_root, $"{prefix}-{part:000}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaxSegmentBytes) return candidate;
        }

        return Path.Combine(_root, $"{prefix}-{Guid.NewGuid():N}.log");
    }

    private static IReadOnlyDictionary<string, string?>? SanitizeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata.Take(40))
        {
            var key = Clean(pair.Key, 100);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (IsSensitiveKey(key))
            {
                result[key] = "[REDACTED]";
                continue;
            }
            result[key] = Clean(pair.Value, 2000);
        }
        return result;
    }

    private static string FileComponent(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static bool IsSensitiveKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("sig", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("access_token", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = value.Trim();
        safe = Regex.Replace(safe, @"(?i)(password|pwd|secret|token|access_token|authorization|cookie|api[_-]?key|sig|signature)\s*[:=]\s*[^\s;,&]+", "$1=[REDACTED]");
        safe = Regex.Replace(safe, @"(?i)Bearer\s+[A-Za-z0-9._~+\-/]+=*", "Bearer [REDACTED]");
        return safe.Length <= max ? safe : safe[..max] + "…";
    }

    private void CleanupOldSegments()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_root, "mam-runtime-*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch
                {
                    // Retention cleanup is best-effort.
                }
            }
        }
        catch
        {
            // Diagnostics cleanup must never prevent startup.
        }
    }

    private static string ResolveWritableRoot(string? explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath.Trim());

        var configured = Environment.GetEnvironmentVariable("MAM_RUNTIME_LOG_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim());

        if (OperatingSystem.IsWindows())
        {
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(common))
                candidates.Add(Path.Combine(common, "DiwanMAM", "Logs", "RuntimeInspector"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            candidates.Add(Path.Combine(local, "DiwanMAM", "Logs", "RuntimeInspector"));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "logs", "runtime-inspector"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "DiwanMAM", "RuntimeInspector"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, $".write-{Environment.ProcessId}-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
                // Try the next safe local path.
            }
        }

        return AppContext.BaseDirectory;
    }
}
