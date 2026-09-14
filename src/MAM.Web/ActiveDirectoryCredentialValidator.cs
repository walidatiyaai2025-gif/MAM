using System.Runtime.InteropServices;

namespace MAM.Web;

internal sealed class ActiveDirectoryCredentialValidator
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    private readonly string _netbiosDomain;
    private readonly string _dnsDomain;

    public ActiveDirectoryCredentialValidator()
    {
        _netbiosDomain = NormalizeDomain(Environment.GetEnvironmentVariable("MAM_AD_NETBIOS_DOMAIN"), "DA");
        _dnsDomain = NormalizeDomain(Environment.GetEnvironmentVariable("MAM_AD_DNS_DOMAIN"), "da.gov.kw");
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

        IntPtr token = IntPtr.Zero;
        try
        {
            var success = LogonUser(
                parsed.Value.LogonUserName,
                parsed.Value.LogonDomain,
                password,
                Logon32LogonNetwork,
                Logon32ProviderDefault,
                out token);

            return success
                ? AdCredentialValidationResult.Succeeded(parsed.Value.CanonicalUserName)
                : AdCredentialValidationResult.Failed("invalid_credentials");
        }
        catch (DllNotFoundException)
        {
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");
        }
        catch (EntryPointNotFoundException)
        {
            return AdCredentialValidationResult.Failed("ad_validation_unavailable");
        }
        finally
        {
            if (token != IntPtr.Zero)
                CloseHandle(token);
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

    private static string NormalizeDomain(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > 128 || normalized.Any(char.IsControl) ? fallback : normalized;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly record struct ParsedUserName(string LogonUserName, string? LogonDomain, string CanonicalUserName);
}

internal sealed record AdCredentialValidationResult(bool Success, string? CanonicalUserName, string ErrorCode)
{
    public static AdCredentialValidationResult Succeeded(string canonicalUserName) => new(true, canonicalUserName, string.Empty);
    public static AdCredentialValidationResult Failed(string code) => new(false, null, code);
}
