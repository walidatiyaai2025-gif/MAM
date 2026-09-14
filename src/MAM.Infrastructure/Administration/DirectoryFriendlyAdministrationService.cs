using System.Data;
using MAM.Application.Administration;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Administration;

/// <summary>
/// Production administration decorator that preserves the P08 policy/dictionary implementation
/// while allowing canonical Windows/Active Directory account names such as DA\username.
/// </summary>
public sealed class DirectoryFriendlyAdministrationService : IAdministrationService
{
    private static readonly IReadOnlySet<string> AllowedRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        MamRoles.Administrator,
        MamRoles.CatalogEditor,
        MamRoles.Viewer
    };

    private readonly SqlServerAdministrationService _inner;
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;

    public DirectoryFriendlyAdministrationService(
        SqlServerAdministrationService inner,
        SqlServerConnectionFactory connections,
        IAuditSink audit)
    {
        _inner = inner;
        _connections = connections;
        _audit = audit;
    }

    public ValueTask<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default) => _inner.GetHealthAsync(cancellationToken);
    public ValueTask<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => _inner.GetOverviewAsync(cancellationToken);
    public ValueTask<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default) => _inner.ListPoliciesAsync(cancellationToken);
    public ValueTask<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default) => _inner.GetPolicyAsync(policyKey, cancellationToken);
    public ValueTask<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default) => _inner.ValidatePolicyAsync(policyKey, request, cancellationToken);
    public ValueTask<AdminPolicyRecord> UpsertPolicyAsync(string policyKey, AdminPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => _inner.UpsertPolicyAsync(policyKey, request, actorId, cancellationToken);
    public ValueTask<AdminConnectionTestResult> TestPolicyAsync(string policyKey, string actorId, CancellationToken cancellationToken = default) => _inner.TestPolicyAsync(policyKey, actorId, cancellationToken);
    public ValueTask<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default) => _inner.ListUsersAsync(cancellationToken);
    public ValueTask<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default) => _inner.ListDictionaryAsync(dictionaryKey, cancellationToken);
    public ValueTask<AdminDictionaryEntry> UpsertDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => _inner.UpsertDictionaryEntryAsync(dictionaryKey, entryKey, request, actorId, cancellationToken);
    public ValueTask<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default) => _inner.QueryAuditAsync(query, cancellationToken);
    public ValueTask<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default) => _inner.ExportAuditCsvAsync(query, cancellationToken);

    public async ValueTask<AdminUserPolicyRecord> UpsertUserAsync(
        Guid userId,
        AdminUserPolicyUpdateRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new AdministrationRequestException("invalid_user", "UserId cannot be empty.", 400);

        actorId = string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim();
        var userName = NormalizeUserName(request.UserName);
        var displayName = RequiredText(request.DisplayName, 300, "Display name");
        var externalSubject = OptionalText(request.ExternalSubject, 200, "External subject");
        var roles = (request.Roles ?? Array.Empty<string>())
            .Select(role => role?.Trim() ?? string.Empty)
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0)
            throw new AdministrationRequestException("role_required", "At least one MAM role must be assigned.", 400);

        if (roles.Any(role => !AllowedRoles.Contains(role)))
        {
            await AuditAsync(actorId, "administration.user.rejected", userId, "Rejected", "Unknown role requested.", cancellationToken);
            throw new AdministrationRequestException("invalid_role", "Only Administrator, CatalogEditor and Viewer roles are allowed.", 400);
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (request.ExpectedVersion == 0)
        {
            const string insert = """
                INSERT dbo.MamUser(UserId,ExternalSubject,UserName,DisplayName,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                VALUES(@Id,@External,@UserName,@Display,@Enabled,@Now,@Now,1);
                """;
            try
            {
                await using var command = new SqlCommand(insert, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
                AddUserParameters(command, userId, userName, displayName, externalSubject, request.IsEnabled, now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await transaction.RollbackAsync(cancellationToken);
                await AuditAsync(actorId, "administration.user.conflict", userId, "Conflict", $"Windows account already exists: {userName}", cancellationToken);
                throw new AdministrationRequestException("user_already_exists", $"The Windows/AD account '{userName}' is already assigned to a MAM user.", 409);
            }
        }
        else
        {
            if (request.ExpectedVersion < 1)
                throw new AdministrationRequestException("invalid_version", "ExpectedVersion must be zero for create or at least one for update.", 400);

            const string update = """
                UPDATE dbo.MamUser
                SET ExternalSubject=@External,UserName=@UserName,DisplayName=@Display,IsEnabled=@Enabled,
                    UpdatedAtUtc=@Now,Version=Version+1
                WHERE UserId=@Id AND Version=@ExpectedVersion;
                """;
            try
            {
                await using var command = new SqlCommand(update, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
                AddUserParameters(command, userId, userName, displayName, externalSubject, request.IsEnabled, now);
                command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
                if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    var current = await ReadUserAsync(connection, transaction, userId, cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                    if (current is null)
                        throw new AdministrationRequestException("user_not_found", "The MAM user no longer exists.", 404);
                    throw new AdministrationRequestException("concurrency_conflict", "The user changed after it was loaded. Refresh the administration page and retry.", 409, current);
                }
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new AdministrationRequestException("user_name_conflict", $"The Windows/AD account '{userName}' is already assigned to another MAM user.", 409);
            }
        }

        await ReplaceRolesAsync(connection, transaction, userId, roles, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var saved = await ReadUserAsync(connection, null, userId, cancellationToken)
            ?? throw new AdministrationRequestException("administration_unavailable", "The saved user could not be read back from the authoritative user store.", 503);

        await AuditAsync(actorId, "administration.user.updated", userId, "Success",
            $"username={saved.UserName};version={saved.Version};roles={string.Join(',', saved.Roles)};enabled={saved.IsEnabled}", cancellationToken);
        return saved;
    }

    private static string NormalizeUserName(string? value)
    {
        var text = RequiredText(value, 200, "Windows / AD account");
        if (text.Any(char.IsControl))
            throw new AdministrationRequestException("invalid_user_name", "Windows / AD account cannot contain control characters.", 400);
        if (text.Contains('/'))
            throw new AdministrationRequestException("invalid_user_name", "Use the Windows account format DOMAIN\\username or user@domain, not a forward slash.", 400);
        if (text.StartsWith('\\') || text.EndsWith('\\') || text.Count(character => character == '\\') > 1)
            throw new AdministrationRequestException("invalid_user_name", "Windows / AD account must use DOMAIN\\username, user@domain, or a simple account name.", 400);
        return text;
    }

    private async ValueTask<AdminUserPolicyRecord?> ReadUserAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.UserId,u.UserName,u.DisplayName,u.ExternalSubject,u.IsEnabled,u.Version,r.RoleName
            FROM dbo.MamUser u
            LEFT JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId
            LEFT JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
            WHERE u.UserId=@Id ORDER BY r.RoleName;
            """;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        UserAccumulator? item = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            item ??= new UserAccumulator(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetInt64(5));
            if (!reader.IsDBNull(6)) item.Roles.Add(reader.GetString(6));
        }
        return item?.ToRecord();
    }

    private async ValueTask ReplaceRolesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid userId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        await using (var delete = new SqlCommand("DELETE dbo.MamUserRole WHERE UserId=@Id;", connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds })
        {
            delete.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var role in roles)
        {
            const string insert = """
                INSERT dbo.MamUserRole(UserId,RoleId)
                SELECT @Id,RoleId FROM dbo.MamRole WHERE RoleName=@Role;
                """;
            await using var command = new SqlCommand(insert, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
            command.Parameters.Add("@Role", SqlDbType.NVarChar, 100).Value = role;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new AdministrationRequestException("role_not_provisioned", $"The role '{role}' is not provisioned in the MAM database.", 400);
        }
    }

    private async ValueTask AuditAsync(string actor, string action, Guid userId, string outcome, string detail, CancellationToken cancellationToken) =>
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, "MamUser", userId.ToString("D"), outcome, detail), cancellationToken);

    private static void AddUserParameters(SqlCommand command, Guid id, string userName, string displayName, string? externalSubject, bool enabled, DateTimeOffset now)
    {
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName;
        command.Parameters.Add("@Display", SqlDbType.NVarChar, 300).Value = displayName;
        command.Parameters.Add("@External", SqlDbType.NVarChar, 200).Value = Db(externalSubject);
        command.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
    }

    private static string RequiredText(string? value, int max, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new AdministrationRequestException("invalid_value", $"{label} is required.", 400);
        if (text.Length > max)
            throw new AdministrationRequestException("invalid_value", $"{label} exceeds {max} characters.", 400);
        return text;
    }

    private static string? OptionalText(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > max)
            throw new AdministrationRequestException("invalid_value", $"{label} exceeds {max} characters.", 400);
        return text;
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private sealed class UserAccumulator
    {
        public UserAccumulator(Guid id, string userName, string displayName, string? externalSubject, bool enabled, long version)
        {
            Id = id;
            UserName = userName;
            DisplayName = displayName;
            ExternalSubject = externalSubject;
            Enabled = enabled;
            Version = version;
        }

        public Guid Id { get; }
        public string UserName { get; }
        public string DisplayName { get; }
        public string? ExternalSubject { get; }
        public bool Enabled { get; }
        public long Version { get; }
        public List<string> Roles { get; } = [];
        public AdminUserPolicyRecord ToRecord() => new(Id, UserName, DisplayName, ExternalSubject, Enabled, Version, Roles.ToArray());
    }
}
