using System.Text.Json;

namespace MAM.Infrastructure.Configuration;

public static class MamSettingsLoader
{
    public static MamSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new MamConfigurationException([$"Configuration file not found: {path}"]);
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<MamSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new MamSettings();

        var errors = MamSettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new MamConfigurationException(errors);
        }

        return settings;
    }
}

public sealed class MamConfigurationException(IReadOnlyList<string> errors)
    : Exception("Invalid MAM configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(static error => $" - {error}")))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
