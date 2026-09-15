using System.DirectoryServices.Protocols;
using System.Net;

namespace MAM.Web;

internal sealed class ActiveDirectoryCredentialValidator
{
    private readonly string _netbiosDomain;
    private readonly string _dnsDomain;
    private readonly string _ldapServer;
    private readonly ILogger<ActiveDirectoryCredentialValidator> _logger;

    public ActiveDirectoryCredentialValidator(ILogger<ActiveDirectoryCredentialValidator> logger)
    {
        _logger = logger;
        _netbiosDomain = NormalizeDomain(Environment.GetEnvironmentVariable("MAM_AD_NETBIOS_DOMAIN"), "DA");
        _dnsDomain = NormalizeDomain(Environment.GetEnvironmentVariable("MAM_AD_DNS_DOMAIN"), "da.gov.kw");
        _ldapServer = NormalizeServer(Environment.GetEnvironmentVariable("MAM_AD_LDAP_SERVER"), _dnsDomain);
    }

    public AdCredentialValidationResult Validate(string? suppliedUserName, string? password)
    {
        if (!OperatingSystem.IsWindows())
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");

        var userName = suppliedUserName?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 200 || userName.Any(char.IsControl))
            return AdCredentialValidationResult.Failed("invalid_credentials");
        if (string.IsNullOrEmpty(password) || password.Length > 1024)
            return AdCredentialValidationResult.Failed("invalid_credentials");

        var parsed = ParseUserName(userName);
        if (parsed is null)
            return AdCredentialValidationResult.Failed("invalid_credentials");

        try
        {
            var identifier = new LdapDirectoryIdentifier(_ldapServer, 389, false, false);
            var credential = string.IsNullOrWhiteSpace(parsed.Value.BindDomain)
                ? new NetworkCredential(parsed.Value.BindUserName, password)
                : new NetworkCredential(parsed.Value.BindUserName, password, parsed.Value.BindDomain);

            using var connection = new LdapConnection(identifier, credential, AuthType.Negotiate)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.Bind();

            return AdCredentialValidationResult.Succeeded(parsed.Value.CanonicalUserName);
        }
        catch (LdapException ex) when (IsDirectoryUnavailable(ex.ErrorCode))
        {
            _logger.LogWarning("Active Directory LDAP validation is unavailable through {Server}; LDAP error {ErrorCode}.", _ldapServer, ex.ErrorCode);
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");
        }
        catch (LdapException ex)
        {
            _logger.LogInformation("Active Directory rejected a form-login credential validation attempt; LDAP error {ErrorCode}.", ex.ErrorCode);
            return AdCredentialValidationResult.Failed("invalid_credentials");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Active Directory LDAP credential validation could not be initialized.");
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");
        }
    }

    private ParsedUserName? ParseUserName(string value)
    {
        var slash = value.IndexOf('\\');
        if (slash >= 0)
        {
            if (slash == 0 || slash != value.LastIndexOf('\\') || slash == value.Length - 1)
                return null;

            var domain = value[..slash].Trim();
            var account = value[(slash + 1)..].Trim();
            if (!domain.Equals(_netbiosDomain, StringComparison.OrdinalIgnoreCase) || !ValidAccount(account))
                return null;

            return new ParsedUserName(account, _netbiosDomain, $"{_netbiosDomain}\\{account}");
        }

        var at = value.LastIndexOf('@');
        if (at >= 0)
        {
            if (at == 0 || at != value.IndexOf('@') || at == value.Length - 1)
                return null;

            var account = value[..at].Trim();
            var domain = value[(at + 1)..].Trim();
            if (!domain.Equals(_dnsDomain, StringComparison.OrdinalIgnoreCase) || !ValidAccount(account))
                return null;

            return new ParsedUserName(value, null, $"{_netbiosDomain}\\{account}");
        }

        if (!ValidAccount(value))
            return null;

        return new ParsedUserName(value, _netbiosDomain, $"{_netbiosDomain}\\{value}");
    }

    private static bool ValidAccount(string value) =>
        value.Length is > 0 and <= 128 &&
        !value.Any(char.IsControl) &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('@');

    private static bool IsDirectoryUnavailable(int errorCode) => errorCode is 51 or 52 or 81 or 82 or 85 or 91;

    private static string NormalizeDomain(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > 128 || normalized.Any(char.IsControl) ? fallback : normalized;
    }

    private static string NormalizeServer(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (normalized.Length > 255 || normalized.Any(char.IsControl) || normalized.Contains('/') || normalized.Contains('\\'))
            return fallback;
        return normalized;
    }

    private readonly record struct ParsedUserName(string BindUserName, string? BindDomain, string CanonicalUserName);
}

internal sealed record AdCredentialValidationResult(bool Success, string? CanonicalUserName, string ErrorCode)
{
    public static AdCredentialValidationResult Succeeded(string canonicalUserName) => new(true, canonicalUserName, string.Empty);
    public static AdCredentialValidationResult Failed(string code) => new(false, null, code);
}
