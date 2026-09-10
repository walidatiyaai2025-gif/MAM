using System.Reflection;

namespace MAM.Application.Diagnostics;

public sealed record BuildInfo(string Version, string CommitSha, string BuildNumber, string BuildTimestampUtc, string EnvironmentName)
{
    public static BuildInfo Current
    {
        get
        {
            var assembly = typeof(BuildInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? assembly.GetName().Version?.ToString()
                                ?? "0.0.0";
            var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                .GroupBy(static item => item.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            return new BuildInfo(
                informational,
                ReadMetadata(metadata, "MamCommitSha", Environment.GetEnvironmentVariable("GITHUB_SHA") ?? Environment.GetEnvironmentVariable("MAM_COMMIT_SHA") ?? "local"),
                ReadMetadata(metadata, "MamBuildNumber", Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ?? "local"),
                ReadMetadata(metadata, "MamBuildTimestampUtc", Environment.GetEnvironmentVariable("MAM_BUILD_TIMESTAMP_UTC") ?? "not-stamped"),
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development");
        }
    }

    public BuildInfo WithEnvironment(string? environmentName) =>
        this with { EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? EnvironmentName : environmentName };

    private static string ReadMetadata(IReadOnlyDictionary<string, string> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
