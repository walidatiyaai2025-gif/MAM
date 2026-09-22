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
    private const long MaxSegmentBytes = 20L * 1024L * 1024L;
    private readonly object _gate = new();
    private readonly string _component;
    private readonly string _root;
    private readonly BuildInfo _build;

    public RuntimeInspectorLog(string component, BuildInfo? build = null, string? rootPath = null)
    {
        _component = Clean(component, 80) ?? "MAM";
        _build = build ?? BuildInfo.Current;
        _root = ResolveWritableRoot(rootPath);
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
        var prefix = $"mam-runtime-{day}";
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

    private static bool IsSensitiveKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("api_key", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = value.Trim();
        safe = Regex.Replace(safe, @"(?i)(password|pwd|secret|token|authorization|cookie|api[_-]?key)\s*[:=]\s*[^\s;,&]+", "$1=[REDACTED]");
        safe = Regex.Replace(safe, @"(?i)Bearer\s+[A-Za-z0-9._~+\-/]+=*", "Bearer [REDACTED]");
        return safe.Length <= max ? safe : safe[..max] + "…";
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
