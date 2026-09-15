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
            using var connection = CreateConnection(
                string.IsNullOrWhiteSpace(parsed.Value.BindDomain)
                    ? new NetworkCredential(parsed.Value.BindUserName, password)
                    : new NetworkCredential(parsed.Value.BindUserName, password, parsed.Value.BindDomain));
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
            var failure = ClassifyCredentialFailure(parsed.Value.AccountName, ex);
            _logger.LogInformation(
                "Active Directory rejected a form-login attempt for account {Account}; LDAP error {ErrorCode}; classified as {FailureCode}.",
                parsed.Value.AccountName, ex.ErrorCode, failure);
            return AdCredentialValidationResult.Failed(failure);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Active Directory LDAP credential validation could not be initialized.");
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");
        }
    }

    private string ClassifyCredentialFailure(string accountName, LdapException exception)
    {
        var subCode = LdapSubCode(exception.ServerErrorMessage);
        var fromSubCode = subCode switch
        {
            "532" => "password_expired",
            "533" => "account_disabled",
            "701" => "account_expired",
            "773" => "password_change_required",
            "775" => "account_locked",
            "530" => "account_logon_restricted",
            "531" => "account_logon_restricted",
            _ => null
        };
        if (fromSubCode is not null) return fromSubCode;

        try
        {
            var state = ReadAccountState(accountName);
            if (state is not null)
            {
                if (state.Locked) return "account_locked";
                if (state.Disabled) return "account_disabled";
                if (state.AccountExpired) return "account_expired";
                if (state.PasswordChangeRequired) return "password_change_required";
                if (state.PasswordExpired) return "password_expired";
            }
        }
        catch (Exception ex) when (ex is LdapException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Could not enrich the rejected AD login with account-state diagnostics.");
        }

        return "invalid_credentials";
    }

    private DirectoryAccountState? ReadAccountState(string accountName)
    {
        using var connection = CreateConnection(CredentialCache.DefaultNetworkCredentials);
        connection.Bind();

        var rootRequest = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
        var root = (SearchResponse)connection.SendRequest(rootRequest, TimeSpan.FromSeconds(5));
        var baseDn = root.Entries.Count > 0 ? Attribute(root.Entries[0], "defaultNamingContext") : null;
        if (string.IsNullOrWhiteSpace(baseDn)) return null;

        var filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeFilter(accountName)}))";
        var request = new SearchRequest(baseDn, filter, SearchScope.Subtree,
            "userAccountControl", "msDS-User-Account-Control-Computed", "pwdLastSet", "accountExpires");
        request.SizeLimit = 1;
        var response = (SearchResponse)connection.SendRequest(request, TimeSpan.FromSeconds(5));
        if (response.Entries.Count == 0) return null;

        var entry = response.Entries[0];
        var uac = IntAttribute(entry, "userAccountControl");
        var computed = IntAttribute(entry, "msDS-User-Account-Control-Computed");
        var pwdLastSet = LongAttribute(entry, "pwdLastSet");
        var accountExpires = LongAttribute(entry, "accountExpires");
        var expired = accountExpires > 0 && accountExpires < long.MaxValue && DateTime.FromFileTimeUtc(accountExpires) <= DateTime.UtcNow;
        return new DirectoryAccountState(
            Disabled: (uac & 0x2) != 0,
            Locked: (computed & 0x10) != 0,
            PasswordExpired: (computed & 0x800000) != 0,
            PasswordChangeRequired: pwdLastSet == 0,
            AccountExpired: expired);
    }

    private LdapConnection CreateConnection(NetworkCredential credential)
    {
        var identifier = new LdapDirectoryIdentifier(_ldapServer, 389, false, false);
        var connection = new LdapConnection(identifier, credential, AuthType.Negotiate)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.Signing = true;
        connection.SessionOptions.Sealing = true;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        return connection;
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

            return new ParsedUserName(account, account, _netbiosDomain, $"{account}@{_dnsDomain}");
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

            return new ParsedUserName(account, value, null, $"{account}@{_dnsDomain}");
        }

        if (!ValidAccount(value))
            return null;

        return new ParsedUserName(value, value, _netbiosDomain, $"{value}@{_dnsDomain}");
    }

    private static string? Attribute(SearchResultEntry entry, string name) =>
        entry.Attributes[name]?.Count > 0 ? entry.Attributes[name]![0]?.ToString() : null;
    private static int IntAttribute(SearchResultEntry entry, string name) => int.TryParse(Attribute(entry, name), out var value) ? value : 0;
    private static long LongAttribute(SearchResultEntry entry, string name) => long.TryParse(Attribute(entry, name), out var value) ? value : 0;

    private static string LdapSubCode(string? serverMessage)
    {
        if (string.IsNullOrWhiteSpace(serverMessage)) return string.Empty;
        var marker = serverMessage.IndexOf("data ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return string.Empty;
        marker += 5;
        var end = marker;
        while (end < serverMessage.Length && Uri.IsHexDigit(serverMessage[end])) end++;
        return end > marker ? serverMessage[marker..end].ToLowerInvariant() : string.Empty;
    }

    private static string EscapeFilter(string value) => value
        .Replace("\\", "\\5c", StringComparison.Ordinal)
        .Replace("*", "\\2a", StringComparison.Ordinal)
        .Replace("(", "\\28", StringComparison.Ordinal)
        .Replace(")", "\\29", StringComparison.Ordinal)
        .Replace("\0", "\\00", StringComparison.Ordinal);

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

    private readonly record struct ParsedUserName(string AccountName, string BindUserName, string? BindDomain, string CanonicalUserName);
    private sealed record DirectoryAccountState(bool Disabled, bool Locked, bool PasswordExpired, bool PasswordChangeRequired, bool AccountExpired);
}

internal sealed record AdCredentialValidationResult(bool Success, string? CanonicalUserName, string ErrorCode)
{
    public static AdCredentialValidationResult Succeeded(string canonicalUserName) => new(true, canonicalUserName, string.Empty);
    public static AdCredentialValidationResult Failed(string code) => new(false, null, code);
}