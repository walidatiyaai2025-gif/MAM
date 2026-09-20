namespace MAM.Application.Identity;

public static class ActiveDirectoryIdentity
{
    public const string DefaultNetbiosDomain = "DA";
    public const string DefaultDnsDomain = "da.gov.kw";

    public static IReadOnlyList<string> ResolveAliases(
        string? userName,
        string? netbiosDomain = null,
        string? dnsDomain = null)
    {
        var value = userName?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Length > 200 || value.Any(char.IsControl))
            return Array.Empty<string>();

        var netbios = NormalizeDomain(netbiosDomain, DefaultNetbiosDomain);
        var dns = NormalizeDomain(dnsDomain, DefaultDnsDomain);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value };

        var slash = value.IndexOf('\\');
        if (slash > 0 && slash == value.LastIndexOf('\\') && slash < value.Length - 1)
        {
            var domain = value[..slash].Trim();
            var account = value[(slash + 1)..].Trim();
            if (domain.Equals(netbios, StringComparison.OrdinalIgnoreCase) && ValidAccount(account))
                AddTrustedAliases(aliases, account, netbios, dns);
            return aliases.ToArray();
        }

        var at = value.LastIndexOf('@');
        if (at > 0 && at == value.IndexOf('@') && at < value.Length - 1)
        {
            var account = value[..at].Trim();
            var domain = value[(at + 1)..].Trim();
            if (domain.Equals(dns, StringComparison.OrdinalIgnoreCase) && ValidAccount(account))
                AddTrustedAliases(aliases, account, netbios, dns);
            return aliases.ToArray();
        }

        if (ValidAccount(value))
            AddTrustedAliases(aliases, value, netbios, dns);

        return aliases.ToArray();
    }

    private static void AddTrustedAliases(HashSet<string> aliases, string account, string netbiosDomain, string dnsDomain)
    {
        aliases.Add(account);
        aliases.Add($"{netbiosDomain}\\{account}");
        aliases.Add($"{account}@{dnsDomain}");
    }

    private static bool ValidAccount(string value) =>
        value.Length is > 0 and <= 128 &&
        !value.Any(char.IsControl) &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('@');

    private static string NormalizeDomain(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > 128 || normalized.Any(char.IsControl) ? fallback : normalized;
    }
}
