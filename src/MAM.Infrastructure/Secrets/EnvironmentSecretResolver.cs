namespace MAM.Infrastructure.Secrets;

public interface ISecretResolver
{
    bool TryResolve(string secretReference, out string secretValue);
    string ResolveRequired(string secretReference);
}

public sealed class EnvironmentSecretResolver : ISecretResolver
{
    public bool TryResolve(string secretReference, out string secretValue)
    {
        secretValue = string.Empty;
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return false;
        }

        var reference = secretReference.Trim();
        string? environmentVariable = null;

        if (reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            environmentVariable = reference[4..].Trim();
        }
        else if (reference.StartsWith("development-user-secrets:", StringComparison.OrdinalIgnoreCase))
        {
            var logicalName = reference["development-user-secrets:".Length..].Trim();
            if (logicalName.Length > 0)
            {
                environmentVariable = $"MAM_SECRET_{NormalizeName(logicalName)}";
            }
        }

        if (string.IsNullOrWhiteSpace(environmentVariable))
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        secretValue = value;
        return true;
    }

    public string ResolveRequired(string secretReference)
    {
        if (TryResolve(secretReference, out var secretValue))
        {
            return secretValue;
        }

        throw new InvalidOperationException(
            "Required secret reference could not be resolved. Configure the server-side secret source; plaintext credentials in application configuration are forbidden.");
    }

    private static string NormalizeName(string logicalName)
    {
        var characters = logicalName
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray();
        return new string(characters);
    }
}
