using System.Text.Json;
using System.Text.Json.Serialization;

namespace MAM.Infrastructure.Configuration;

public static class MamSettingsLoader
{
    public static MamSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new MamConfigurationException([$"Configuration file not found: {path}"]);
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<MamSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? new MamSettings();

            var errors = MamSettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                throw new MamConfigurationException(errors);
            }

            return settings;
        }
        catch (MamConfigurationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new MamConfigurationException([$"Configuration schema error: {ex.Message}"]);
        }
    }
}

public sealed class MamConfigurationException(IReadOnlyList<string> errors)
    : Exception("Invalid MAM configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(static error => $" - {error}")))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
