using System.Reflection;

namespace MAM.Infrastructure.Diagnostics;

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

            return new BuildInfo(
                informational,
                Environment.GetEnvironmentVariable("GITHUB_SHA") ?? Environment.GetEnvironmentVariable("MAM_COMMIT_SHA") ?? "local",
                Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ?? "local",
                Environment.GetEnvironmentVariable("MAM_BUILD_TIMESTAMP_UTC") ?? "not-stamped",
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development");
        }
    }
}
