using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using MAM.Application.Identity;
using MAM.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace MAM.Api.Security;

public sealed class MamAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Mam";
    public const string DevelopmentUserHeader = "X-MAM-Dev-User";
    public const string ProductionUserHeader = "X-MAM-Auth-User";
    public const string ProductionTimestampHeader = "X-MAM-Auth-Timestamp";
    public const string ProductionSignatureHeader = "X-MAM-Auth-Signature";
    private static readonly TimeSpan SignatureLifetime = TimeSpan.FromMinutes(2);

    private readonly MamSettings _settings;

    public MamAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,MamSettings settings) : base(options, logger, encoder) => _settings = settings;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var localDemoOrDevelopment = (string.Equals(_settings.Environment.Name, "Development", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(_settings.Environment.Name, "Demo", StringComparison.OrdinalIgnoreCase))
                                     && string.Equals(_settings.Auth.Mode, "Local", StringComparison.OrdinalIgnoreCase);
        if (localDemoOrDevelopment) return HandleLocal();
        if (string.Equals(_settings.Auth.Mode, "ActiveDirectory", StringComparison.OrdinalIgnoreCase)) return await HandleActiveDirectoryAsync(Context.RequestAborted);
        return AuthenticateResult.NoResult();
    }

    private AuthenticateResult HandleLocal()
    {
        if (!Request.Headers.TryGetValue(DevelopmentUserHeader, out var headerValue) || string.IsNullOrWhiteSpace(headerValue)) return AuthenticateResult.NoResult();
        var demo = string.Equals(_settings.Environment.Name, "Demo", StringComparison.OrdinalIgnoreCase);
        var identity = headerValue.ToString().Trim().ToLowerInvariant() switch
        {
            "admin" => CreateIdentity(demo ? "demo-admin" : "dev-admin", demo ? "Demo Administrator" : "Development Administrator", [MamRoles.Administrator]),
            "editor" => CreateIdentity(demo ? "demo-editor" : "dev-editor", demo ? "Demo Catalog Editor" : "Development Catalog Editor", [MamRoles.CatalogEditor]),
            "viewer" => CreateIdentity(demo ? "demo-viewer" : "dev-viewer", demo ? "Demo Viewer" : "Development Viewer", [MamRoles.Viewer]),
            _ => null
        };
        if (identity is null) return AuthenticateResult.Fail("Unknown local identity.");
        return Success(identity);
    }

    private async Task<AuthenticateResult> HandleActiveDirectoryAsync(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(ProductionUserHeader, out var userHeader) || !Request.Headers.TryGetValue(ProductionTimestampHeader, out var timestampHeader) || !Request.Headers.TryGetValue(ProductionSignatureHeader, out var signatureHeader)) return AuthenticateResult.NoResult();
        var userName = userHeader.ToString().Trim();
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 200) return AuthenticateResult.Fail("Invalid production identity.");
        if (!long.TryParse(timestampHeader.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)) return AuthenticateResult.Fail("Invalid production identity timestamp.");
        DateTimeOffset issuedAt; try { issuedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); } catch (ArgumentOutOfRangeException) { return AuthenticateResult.Fail("Invalid production identity timestamp."); }
        if ((DateTimeOffset.UtcNow - issuedAt).Duration() > SignatureLifetime) return AuthenticateResult.Fail("Expired production identity signature.");
        var encodedKey = Environment.GetEnvironmentVariable("MAM_INTERNAL_AUTH_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey)) return AuthenticateResult.Fail("Production identity authority is unavailable.");
        byte[] key; byte[] supplied;
        try { key = Convert.FromBase64String(encodedKey); supplied = Convert.FromBase64String(signatureHeader.ToString()); }
        catch (FormatException) { return AuthenticateResult.Fail("Invalid production identity signature."); }
        if (key.Length < 32) { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(supplied); return AuthenticateResult.Fail("Production identity authority is invalid."); }
        var target = Request.PathBase.Add(Request.Path).ToString() + Request.QueryString.ToString();
        var canonical = string.Join('\n', userName, timestampHeader.ToString(), Request.Method.ToUpperInvariant(), target);
        byte[] expected; using (var hmac = new HMACSHA256(key)) expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        var signatureValid = supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
        CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(supplied); CryptographicOperations.ZeroMemory(expected);
        if (!signatureValid) return AuthenticateResult.Fail("Invalid production identity signature.");
        var connectionString = Environment.GetEnvironmentVariable("MAM_SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) return AuthenticateResult.Fail("Authoritative user store is unavailable.");
        try
        {
            await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken); var user = await ReadUserAsync(connection, userName, cancellationToken);
            if (user is null && IsBootstrapAdministrator(userName)) { await ProvisionBootstrapAdministratorAsync(connection, userName, cancellationToken); user = await ReadUserAsync(connection, userName, cancellationToken); }
            if (user is null) return AuthenticateResult.Fail("This Windows account has not been granted MAM access.");
            if (!user.IsEnabled) return AuthenticateResult.Fail("This MAM user is disabled.");
            if (user.Roles.Count == 0) return AuthenticateResult.Fail("This MAM user has no assigned role.");
            return Success(CreateIdentity(user.UserId.ToString("D"), user.DisplayName, user.Roles, user.UserName));
        }
        catch (SqlException ex) { Logger.LogError(ex, "Production identity lookup failed for {UserName}.", userName); return AuthenticateResult.Fail("Authoritative user store is unavailable."); }
    }

    private bool IsBootstrapAdministrator(string userName) => _settings.Auth.BootstrapAdministrators.Any(item => string.Equals(item?.Trim(), userName, StringComparison.OrdinalIgnoreCase));

    private static async Task<ResolvedUser?> ReadUserAsync(SqlConnection connection, string userName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.UserId,u.UserName,u.DisplayName,u.IsEnabled,r.RoleName
            FROM dbo.MamUser u LEFT JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId LEFT JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
            WHERE u.UserName=@UserName OR u.ExternalSubject=@UserName ORDER BY r.RoleName;
            """;
        await using var command = new SqlCommand(sql, connection); command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName; await using var reader = await command.ExecuteReaderAsync(cancellationToken); ResolvedUserBuilder? builder = null;
        while (await reader.ReadAsync(cancellationToken)) { builder ??= new ResolvedUserBuilder(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)); if (!reader.IsDBNull(4)) builder.Roles.Add(reader.GetString(4)); }
        return builder?.Build();
    }

    private static async Task ProvisionBootstrapAdministratorAsync(SqlConnection connection, string userName, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var userId = Guid.NewGuid();
            const string insertUser = """
                IF NOT EXISTS (SELECT 1 FROM dbo.MamUser WHERE UserName=@UserName OR ExternalSubject=@UserName)
                INSERT dbo.MamUser(UserId,ExternalSubject,UserName,DisplayName,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                VALUES(@UserId,@UserName,@UserName,@UserName,1,SYSUTCDATETIME(),SYSUTCDATETIME(),1);
                """;
            await using (var command = new SqlCommand(insertUser, connection, transaction)) { command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId; command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName; await command.ExecuteNonQueryAsync(cancellationToken); }
            const string grant = """
                INSERT dbo.MamUserRole(UserId,RoleId)
                SELECT u.UserId,r.RoleId FROM dbo.MamUser u CROSS JOIN dbo.MamRole r
                WHERE (u.UserName=@UserName OR u.ExternalSubject=@UserName) AND r.RoleName=N'Administrator'
                  AND NOT EXISTS (SELECT 1 FROM dbo.MamUserRole ur WHERE ur.UserId=u.UserId AND ur.RoleId=r.RoleId);
                """;
            await using (var command = new SqlCommand(grant, connection, transaction)) { command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName; await command.ExecuteNonQueryAsync(cancellationToken); }
            const string audit = """
                INSERT dbo.MamAuditEvent(AuditEventId,OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail)
                SELECT NEWID(),SYSUTCDATETIME(),@UserName,N'identity.bootstrap-administrator',N'MamUser',CONVERT(nvarchar(36),u.UserId),N'Success',N'Provisioned from setup bootstrap administrator allowlist.'
                FROM dbo.MamUser u WHERE u.UserName=@UserName OR u.ExternalSubject=@UserName;
                """;
            await using (var command = new SqlCommand(audit, connection, transaction)) { command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName; await command.ExecuteNonQueryAsync(cancellationToken); }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private static ClaimsIdentity CreateIdentity(string userId, string displayName, IReadOnlyCollection<string> roles, string? userName = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, displayName) };
        if (!string.IsNullOrWhiteSpace(userName)) claims.Add(new(ClaimTypes.WindowsAccountName, userName));
        foreach (var role in roles.Distinct(StringComparer.Ordinal)) { claims.Add(new(ClaimTypes.Role, role)); claims.AddRange(MamSecurity.PermissionsForRole(role).Select(permission => new Claim(MamSecurity.PermissionClaimType, permission))); }
        return new ClaimsIdentity(claims, SchemeName);
    }

    private static AuthenticateResult Success(ClaimsIdentity identity) { var principal = new ClaimsPrincipal(identity); var ticket = new AuthenticationTicket(principal, SchemeName); return AuthenticateResult.Success(ticket); }
    private sealed record ResolvedUser(Guid UserId,string UserName,string DisplayName,bool IsEnabled,IReadOnlyList<string> Roles);
    private sealed class ResolvedUserBuilder(Guid userId,string userName,string displayName,bool isEnabled)
    {
        public Guid UserId { get; }=userId; public string UserName { get; }=userName; public string DisplayName { get; }=displayName; public bool IsEnabled { get; }=isEnabled; public List<string> Roles { get; }=[]; public ResolvedUser Build()=>new(UserId,UserName,DisplayName,IsEnabled,Roles.ToArray());
    }
}
