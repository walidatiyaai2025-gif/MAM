using System.Data;
using System.Text;
using System.Text.Json;
using MAM.Application.Administration;
using MAM.Application.Auditing;
using MAM.Application.Branding;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Secrets;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Administration;

public sealed class SqlServerAdministrationService : IAdministrationService
{
    private static readonly IReadOnlySet<string> AllowedRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        MamRoles.Administrator, MamRoles.CatalogEditor, MamRoles.Viewer
    };

    private static readonly IReadOnlySet<string> SecretPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "clientSecret", "apiKey", "token", "accessToken", "refreshToken",
        "connectionString", "privateKey", "secret", "credential", "credentials"
    };

    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;
    private readonly ISecretResolver _secrets;

    public SqlServerAdministrationService(SqlServerConnectionFactory connections, IAuditSink audit)
    {
        _connections = connections;
        _audit = audit;
        _secrets = new EnvironmentSecretResolver();
    }

    public async ValueTask<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0007_p08_administration';";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count == 1
                ? new AdministrationHealth(true, "SqlServer", "P08 authoritative administration policy store is reachable.")
                : new AdministrationHealth(false, "SqlServer", "SQL Server is reachable but the P08 administration migration is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new AdministrationHealth(false, "SqlServer", $"Authoritative administration store is unavailable: {ex.GetType().Name}.");
        }
    }

    public async ValueTask<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.MamAdminPolicy),
              (SELECT COUNT(*) FROM dbo.MamAdminPolicy WHERE IsEnabled=1),
              (SELECT COUNT(*) FROM dbo.MamUser),
              (SELECT COUNT(*) FROM dbo.MamAdminDictionaryEntry),
              (SELECT COUNT(*) FROM dbo.MamAdminPolicy WHERE IsEnabled=1 AND RequiresRestart=1);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new AdministrationOverview(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), DateTimeOffset.UtcNow);
    }

    public async ValueTask<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, Version, RequiresRestart, IsEnabled, UpdatedAtUtc
            FROM dbo.MamAdminPolicy
            ORDER BY Category, PolicyKey;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminPolicyRecord>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadPolicy(reader));
        return result;
    }

    public async ValueTask<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default)
    {
        policyKey = NormalizeKey(policyKey, "Policy key", 160);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await ReadPolicyAsync(connection, null, policyKey, cancellationToken);
    }

    public ValueTask<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default)
    {
        policyKey = NormalizeKey(policyKey, "Policy key", 160);
        return ValueTask.FromResult(Validate(policyKey, request));
    }

    public async ValueTask<AdminPolicyRecord> UpsertPolicyAsync(string policyKey, AdminPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        policyKey = NormalizeKey(policyKey, "Policy key", 160);
        actorId = NormalizeActor(actorId);
        var validation = Validate(policyKey, request);
        if (!validation.Valid)
        {
            await AuditAsync(actorId, "administration.policy.rejected", "AdminPolicy", policyKey, "Rejected", string.Join(" | ", validation.Errors), cancellationToken);
            throw new AdministrationRequestException("invalid_policy", string.Join(" ", validation.Errors), 400);
        }

        var category = CanonicalCategory(request.Category);
        var nameEn = RequiredText(request.DisplayNameEn, 200, "English display name");
        var nameAr = RequiredText(request.DisplayNameAr, 200, "Arabic display name");
        var payload = request.Payload.GetRawText();
        var secretRef = NormalizeSecretRef(request.SecretRef);
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _connections.OpenAsync(cancellationToken);
        if (request.ExpectedVersion == 0)
        {
            const string insert = """
                INSERT dbo.MamAdminPolicy(PolicyKey, Category, DisplayNameEn, DisplayNameAr, PayloadJson, SecretRef, Version, RequiresRestart, IsEnabled, UpdatedAtUtc)
                VALUES(@Key,@Category,@En,@Ar,@Payload,@SecretRef,1,@Restart,@Enabled,@Updated);
                """;
            try
            {
                await using var command = new SqlCommand(insert, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
                AddPolicyParameters(command, policyKey, category, nameEn, nameAr, payload, secretRef, request.RequiresRestart, request.IsEnabled, now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                var current = await ReadPolicyAsync(connection, null, policyKey, cancellationToken);
                await AuditAsync(actorId, "administration.policy.conflict", "AdminPolicy", policyKey, "Conflict", "Create requested for an existing policy.", cancellationToken);
                throw new AdministrationRequestException("concurrency_conflict", "Policy already exists. Refresh and retry with its current version.", 409, current);
            }
        }
        else
        {
            if (request.ExpectedVersion < 1) throw new AdministrationRequestException("invalid_version", "ExpectedVersion cannot be negative.", 400);
            const string update = """
                UPDATE dbo.MamAdminPolicy
                SET Category=@Category, DisplayNameEn=@En, DisplayNameAr=@Ar, PayloadJson=@Payload, SecretRef=@SecretRef,
                    Version=Version+1, RequiresRestart=@Restart, IsEnabled=@Enabled, UpdatedAtUtc=@Updated
                WHERE PolicyKey=@Key AND Version=@ExpectedVersion;
                """;
            await using var command = new SqlCommand(update, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            AddPolicyParameters(command, policyKey, category, nameEn, nameAr, payload, secretRef, request.RequiresRestart, request.IsEnabled, now);
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                var current = await ReadPolicyAsync(connection, null, policyKey, cancellationToken);
                await AuditAsync(actorId, "administration.policy.conflict", "AdminPolicy", policyKey, "Conflict", $"expectedVersion={request.ExpectedVersion}", cancellationToken);
                if (current is null) throw new AdministrationRequestException("policy_not_found", "Policy was not found.", 404);
                throw new AdministrationRequestException("concurrency_conflict", "Policy changed since it was loaded. Refresh and retry.", 409, current);
            }
        }

        var saved = await ReadPolicyAsync(connection, null, policyKey, cancellationToken)
            ?? throw new AdministrationRequestException("administration_unavailable", "Saved policy could not be re-read.", 503);
        await AuditAsync(actorId, "administration.policy.updated", "AdminPolicy", policyKey, "Success", $"version={saved.Version};category={saved.Category};restart={saved.RequiresRestart}", cancellationToken);
        return saved;
    }

    public async ValueTask<AdminConnectionTestResult> TestPolicyAsync(string policyKey, string actorId, CancellationToken cancellationToken = default)
    {
        policyKey = NormalizeKey(policyKey, "Policy key", 160);
        actorId = NormalizeActor(actorId);
        var policy = await GetPolicyAsync(policyKey, cancellationToken)
            ?? throw new AdministrationRequestException("policy_not_found", "Policy was not found.", 404);

        var hasRef = !string.IsNullOrWhiteSpace(policy.SecretRef);
        var resolvable = hasRef && _secrets.TryResolve(policy.SecretRef!, out _);
        var targetId = TryString(policy.Payload, "targetId") ?? TryString(policy.Payload, "mode") ?? policy.Category;
        AdminConnectionTestResult result;
        if (hasRef)
        {
            result = resolvable
                ? new(true, "secret_reference_resolved", "The server-side secret reference resolved successfully. Secret material was not returned.", targetId, true, true)
                : new(false, "secret_reference_unresolved", "The configured server-side secret reference could not be resolved in this environment.", targetId, true, false);
        }
        else
        {
            result = policy.Category is AdminPolicyCategories.Storage or AdminPolicyCategories.Auth
                ? new(false, "secret_reference_required", "This policy category requires an opaque server-side secret reference before activation.", targetId, false, false)
                : new(true, "validation_only", "The policy has no secret-backed connection; structural validation is successful.", targetId, false, false);
        }

        await AuditAsync(actorId, "administration.policy.tested", "AdminPolicy", policyKey, result.Success ? "Success" : "Failed", $"code={result.Code};secretRefConfigured={result.SecretReferenceConfigured};resolvable={result.SecretReferenceResolvable}", cancellationToken);
        return result;
    }

    public async ValueTask<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT u.UserId,u.UserName,u.DisplayName,u.ExternalSubject,u.IsEnabled,u.Version,r.RoleName
            FROM dbo.MamUser u
            LEFT JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId
            LEFT JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
            ORDER BY u.UserName,r.RoleName;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new Dictionary<Guid, UserAccumulator>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (!rows.TryGetValue(id, out var item))
            {
                item = new UserAccumulator(id, reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5));
                rows[id] = item;
            }
            if (!reader.IsDBNull(6)) item.Roles.Add(reader.GetString(6));
        }
        return rows.Values.Select(x => x.ToRecord()).ToArray();
    }

    public async ValueTask<AdminUserPolicyRecord> UpsertUserAsync(Guid userId, AdminUserPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new AdministrationRequestException("invalid_user", "UserId cannot be empty.", 400);
        actorId = NormalizeActor(actorId);
        var userName = NormalizeKey(request.UserName, "User name", 200);
        var displayName = RequiredText(request.DisplayName, 300, "Display name");
        var externalSubject = OptionalText(request.ExternalSubject, 200, "External subject");
        var roles = (request.Roles ?? Array.Empty<string>()).Select(r => r?.Trim() ?? string.Empty).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (roles.Any(role => !AllowedRoles.Contains(role)))
        {
            await AuditAsync(actorId, "administration.user.rejected", "MamUser", userId.ToString("D"), "Rejected", "Unknown role requested.", cancellationToken);
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
                await AuditAsync(actorId, "administration.user.conflict", "MamUser", userId.ToString("D"), "Conflict", "User identity or username already exists.", cancellationToken);
                throw new AdministrationRequestException("concurrency_conflict", "User already exists or username is already assigned.", 409);
            }
        }
        else
        {
            const string update = """
                UPDATE dbo.MamUser SET ExternalSubject=@External,UserName=@UserName,DisplayName=@Display,IsEnabled=@Enabled,UpdatedAtUtc=@Now,Version=Version+1
                WHERE UserId=@Id AND Version=@ExpectedVersion;
                """;
            await using var command = new SqlCommand(update, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
            AddUserParameters(command, userId, userName, displayName, externalSubject, request.IsEnabled, now);
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                var current = await ReadUserAsync(connection, transaction, userId, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                await AuditAsync(actorId, "administration.user.conflict", "MamUser", userId.ToString("D"), "Conflict", $"expectedVersion={request.ExpectedVersion}", cancellationToken);
                if (current is null) throw new AdministrationRequestException("user_not_found", "User policy record was not found.", 404);
                throw new AdministrationRequestException("concurrency_conflict", "User policy changed since it was loaded.", 409, current);
            }
        }

        await ReplaceRolesAsync(connection, transaction, userId, roles, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var saved = await ReadUserAsync(connection, null, userId, cancellationToken)
            ?? throw new AdministrationRequestException("administration_unavailable", "Saved user policy could not be re-read.", 503);
        await AuditAsync(actorId, "administration.user.updated", "MamUser", userId.ToString("D"), "Success", $"version={saved.Version};roles={string.Join(',', saved.Roles)};enabled={saved.IsEnabled}", cancellationToken);
        return saved;
    }

    public async ValueTask<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default)
    {
        dictionaryKey = NormalizeKey(dictionaryKey, "Dictionary key", 120);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc
            FROM dbo.MamAdminDictionaryEntry WHERE DictionaryKey=@DictionaryKey ORDER BY EntryKey;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@DictionaryKey", SqlDbType.NVarChar, 120).Value = dictionaryKey;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminDictionaryEntry>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDictionary(reader));
        return result;
    }

    public async ValueTask<AdminDictionaryEntry> UpsertDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        dictionaryKey = NormalizeKey(dictionaryKey, "Dictionary key", 120);
        entryKey = NormalizeKey(entryKey, "Entry key", 120);
        actorId = NormalizeActor(actorId);
        var en = RequiredText(request.LabelEn, 300, "English label");
        var ar = RequiredText(request.LabelAr, 300, "Arabic label");
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        if (request.ExpectedVersion == 0)
        {
            const string insert = "INSERT dbo.MamAdminDictionaryEntry(DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc) VALUES(@D,@E,@En,@Ar,@Enabled,1,@Now);";
            try
            {
                await using var command = new SqlCommand(insert, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
                AddDictionaryParameters(command, dictionaryKey, entryKey, en, ar, request.IsEnabled, now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                var current = await ReadDictionaryAsync(connection, dictionaryKey, entryKey, cancellationToken);
                await AuditAsync(actorId, "administration.dictionary.conflict", "DictionaryEntry", $"{dictionaryKey}/{entryKey}", "Conflict", "Create requested for existing entry.", cancellationToken);
                throw new AdministrationRequestException("concurrency_conflict", "Dictionary entry already exists.", 409, current);
            }
        }
        else
        {
            const string update = """
                UPDATE dbo.MamAdminDictionaryEntry SET LabelEn=@En,LabelAr=@Ar,IsEnabled=@Enabled,Version=Version+1,UpdatedAtUtc=@Now
                WHERE DictionaryKey=@D AND EntryKey=@E AND Version=@ExpectedVersion;
                """;
            await using var command = new SqlCommand(update, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
            AddDictionaryParameters(command, dictionaryKey, entryKey, en, ar, request.IsEnabled, now);
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                var current = await ReadDictionaryAsync(connection, dictionaryKey, entryKey, cancellationToken);
                await AuditAsync(actorId, "administration.dictionary.conflict", "DictionaryEntry", $"{dictionaryKey}/{entryKey}", "Conflict", $"expectedVersion={request.ExpectedVersion}", cancellationToken);
                if (current is null) throw new AdministrationRequestException("dictionary_entry_not_found", "Dictionary entry was not found.", 404);
                throw new AdministrationRequestException("concurrency_conflict", "Dictionary entry changed since it was loaded.", 409, current);
            }
        }
        var saved = await ReadDictionaryAsync(connection, dictionaryKey, entryKey, cancellationToken)
            ?? throw new AdministrationRequestException("administration_unavailable", "Saved dictionary entry could not be re-read.", 503);
        await AuditAsync(actorId, "administration.dictionary.updated", "DictionaryEntry", $"{dictionaryKey}/{entryKey}", "Success", $"version={saved.Version};enabled={saved.IsEnabled}", cancellationToken);
        return saved;
    }

    public async ValueTask<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new AdminAuditQuery();
        var limit = Math.Clamp(query.Limit, 1, 500);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var sql = new StringBuilder("SELECT TOP (@Limit) AuditEventId,OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail FROM dbo.MamAuditEvent WHERE 1=1");
        await using var command = new SqlCommand { Connection = connection, CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        if (!string.IsNullOrWhiteSpace(query.Actor)) { sql.Append(" AND ActorId=@Actor"); command.Parameters.Add("@Actor", SqlDbType.NVarChar, 200).Value = query.Actor.Trim(); }
        if (!string.IsNullOrWhiteSpace(query.Action)) { sql.Append(" AND Action LIKE @Action"); command.Parameters.Add("@Action", SqlDbType.NVarChar, 220).Value = "%" + query.Action.Trim() + "%"; }
        if (!string.IsNullOrWhiteSpace(query.Outcome)) { sql.Append(" AND Outcome=@Outcome"); command.Parameters.Add("@Outcome", SqlDbType.NVarChar, 50).Value = query.Outcome.Trim(); }
        if (query.FromUtc is DateTimeOffset from) { sql.Append(" AND OccurredAtUtc>=@From"); command.Parameters.Add("@From", SqlDbType.DateTime2).Value = from.UtcDateTime; }
        if (query.ToUtc is DateTimeOffset to) { sql.Append(" AND OccurredAtUtc<=@To"); command.Parameters.Add("@To", SqlDbType.DateTime2).Value = to.UtcDateTime; }
        sql.Append(" ORDER BY OccurredAtUtc DESC,AuditEventId DESC;");
        command.CommandText = sql.ToString();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadAudit(reader));
        return new AdminAuditResult(items, limit, DateTimeOffset.UtcNow);
    }

    public async ValueTask<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
    {
        var result = await QueryAuditAsync(query, cancellationToken);
        var csv = new StringBuilder("OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail\r\n");
        foreach (var item in result.Items)
        {
            csv.Append(Csv(item.OccurredAtUtc.ToString("O"))).Append(',')
               .Append(Csv(item.ActorId)).Append(',').Append(Csv(item.Action)).Append(',')
               .Append(Csv(item.EntityType)).Append(',').Append(Csv(item.EntityId)).Append(',')
               .Append(Csv(item.Outcome)).Append(',').Append(Csv(item.Detail ?? string.Empty)).Append("\r\n");
        }
        return csv.ToString();
    }

    private static AdminPolicyValidationResult Validate(string policyKey, AdminPolicyUpdateRequest request)
    {
        var errors = new List<string>();
        if (request is null) return new(false, ["Policy body is required."], false);
        if (!AdminPolicyCategories.All.Contains(request.Category ?? string.Empty)) errors.Add("Policy category is not supported.");
        if (string.IsNullOrWhiteSpace(request.DisplayNameEn) || request.DisplayNameEn.Trim().Length > 200) errors.Add("English display name is required and limited to 200 characters.");
        if (string.IsNullOrWhiteSpace(request.DisplayNameAr) || request.DisplayNameAr.Trim().Length > 200) errors.Add("Arabic display name is required and limited to 200 characters.");
        if (request.ExpectedVersion < 0) errors.Add("ExpectedVersion cannot be negative.");
        if (request.Payload.ValueKind != JsonValueKind.Object) errors.Add("Policy payload must be a JSON object.");
        else FindInlineSecrets(request.Payload, "$", errors);

        var category = request.Category?.Trim() ?? string.Empty;
        var secretRef = request.SecretRef?.Trim();
        if (category is AdminPolicyCategories.Storage or AdminPolicyCategories.Auth)
        {
            if (string.IsNullOrWhiteSpace(secretRef)) errors.Add("Storage/Auth policies require an opaque SecretRef.");
        }
        if (!string.IsNullOrWhiteSpace(secretRef) && !IsAllowedSecretRef(secretRef)) errors.Add("SecretRef must use env: or development-user-secrets: without embedding a secret value.");

        if (request.Payload.ValueKind == JsonValueKind.Object)
        {
            if (category == AdminPolicyCategories.Retention)
            {
                if (!TryInt(request.Payload, "retentionDays", out var days) || days is < 1 or > 36500) errors.Add("Retention policy requires retentionDays between 1 and 36500.");
                var mode = TryString(request.Payload, "deleteMode");
                if (mode is not ("OwnerApprovedDelete" or "ArchiveOnly" or "LegalHoldOnly")) errors.Add("Retention deleteMode must be OwnerApprovedDelete, ArchiveOnly or LegalHoldOnly.");
            }
            else if (category == AdminPolicyCategories.Branding)
            {
                if (TryString(request.Payload, "crestSha256") is { } crest && !crest.Equals(BrandTokens.CrestSha256, StringComparison.OrdinalIgnoreCase)) errors.Add("Approved Diwan crest fingerprint cannot be changed in P08.");
                if (TryString(request.Payload, "navy") is { } navy && !navy.Equals(BrandTokens.Navy900, StringComparison.OrdinalIgnoreCase)) errors.Add("Approved Diwan Navy token cannot be changed in P08.");
                if (TryString(request.Payload, "gold") is { } gold && !gold.Equals(BrandTokens.Gold600, StringComparison.OrdinalIgnoreCase)) errors.Add("Approved Diwan Gold token cannot be changed in P08.");
                if (TryBool(request.Payload, "identityLocked", out var locked) && !locked) errors.Add("Diwan Al Amiri identity must remain locked.");
            }
            else if (category == AdminPolicyCategories.Capture)
            {
                if (TryBool(request.Payload, "simulatorAllowedProduction", out var simulator) && simulator) errors.Add("Capture simulator cannot be enabled in Production.");
                if (string.IsNullOrWhiteSpace(TryString(request.Payload, "stationPolicyId"))) errors.Add("Capture policy requires stationPolicyId.");
            }
            else if (category == AdminPolicyCategories.Processing)
            {
                if (string.IsNullOrWhiteSpace(TryString(request.Payload, "profileId"))) errors.Add("Processing policy requires profileId.");
                if (!TryInt(request.Payload, "version", out var version) || version < 1) errors.Add("Processing policy version must be positive.");
            }
            else if (category == AdminPolicyCategories.Identity)
            {
                if (TryBool(request.Payload, "localPasswordsAllowed", out var local) && local) errors.Add("Local plaintext/password authority is forbidden; production identity remains external.");
            }
            else if (category == AdminPolicyCategories.Storage)
            {
                if (string.IsNullOrWhiteSpace(TryString(request.Payload, "targetId"))) errors.Add("Storage policy requires a safe targetId.");
            }
        }
        return new(errors.Count == 0, errors, request.RequiresRestart);
    }

    private static void FindInlineSecrets(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (SecretPropertyNames.Contains(property.Name)) errors.Add($"Inline secret-like property is forbidden at {path}.{property.Name}; use SecretRef.");
                FindInlineSecrets(property.Value, path + "." + property.Name, errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray()) FindInlineSecrets(child, $"{path}[{index++}]", errors);
        }
    }

    private static bool IsAllowedSecretRef(string reference)
    {
        if (reference.Length > 300 || reference.Any(char.IsWhiteSpace)) return false;
        if (!(reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("development-user-secrets:", StringComparison.OrdinalIgnoreCase))) return false;
        var separator = reference.IndexOf(':');
        return separator >= 0 && separator < reference.Length - 1 && reference[(separator + 1)..].Length >= 2;
    }

    private static string CanonicalCategory(string category) =>
        AdminPolicyCategories.All.First(x => x.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeSecretRef(string? secretRef) => string.IsNullOrWhiteSpace(secretRef) ? null : secretRef.Trim();
    private static string NormalizeActor(string actorId) => string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim();

    private static string NormalizeKey(string? value, string label, int max)
    {
        var text = RequiredText(value, max, label);
        if (text.Any(char.IsWhiteSpace) || text.Contains('/') || text.Contains('\\')) throw new AdministrationRequestException("invalid_key", $"{label} cannot contain whitespace or path separators.", 400);
        return text;
    }

    private static string RequiredText(string? value, int max, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new AdministrationRequestException("invalid_value", $"{label} is required.", 400);
        if (text.Length > max) throw new AdministrationRequestException("invalid_value", $"{label} exceeds {max} characters.", 400);
        return text;
    }

    private static string? OptionalText(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > max) throw new AdministrationRequestException("invalid_value", $"{label} exceeds {max} characters.", 400);
        return text;
    }

    private async ValueTask<AdminPolicyRecord?> ReadPolicyAsync(SqlConnection connection, SqlTransaction? transaction, string key, CancellationToken cancellationToken)
    {
        const string sql = "SELECT PolicyKey,Category,DisplayNameEn,DisplayNameAr,PayloadJson,SecretRef,Version,RequiresRestart,IsEnabled,UpdatedAtUtc FROM dbo.MamAdminPolicy WHERE PolicyKey=@Key;";
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Key", SqlDbType.NVarChar, 160).Value = key;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    private static AdminPolicyRecord ReadPolicy(SqlDataReader reader)
    {
        using var doc = JsonDocument.Parse(reader.GetString(4));
        return new AdminPolicyRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), doc.RootElement.Clone(), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.GetBoolean(7), reader.GetBoolean(8), Utc(reader.GetDateTime(9)));
    }

    private async ValueTask<AdminUserPolicyRecord?> ReadUserAsync(SqlConnection connection, SqlTransaction? transaction, Guid userId, CancellationToken cancellationToken)
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
            item ??= new UserAccumulator(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5));
            if (!reader.IsDBNull(6)) item.Roles.Add(reader.GetString(6));
        }
        return item?.ToRecord();
    }

    private async ValueTask ReplaceRolesAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, IReadOnlyList<string> roles, CancellationToken cancellationToken)
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
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new AdministrationRequestException("invalid_role", $"Role {role} is not provisioned.", 400);
        }
    }

    private async ValueTask<AdminDictionaryEntry?> ReadDictionaryAsync(SqlConnection connection, string dictionaryKey, string entryKey, CancellationToken cancellationToken)
    {
        const string sql = "SELECT DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc FROM dbo.MamAdminDictionaryEntry WHERE DictionaryKey=@D AND EntryKey=@E;";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.Add("@D", SqlDbType.NVarChar, 120).Value = dictionaryKey;
        command.Parameters.Add("@E", SqlDbType.NVarChar, 120).Value = entryKey;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDictionary(reader) : null;
    }

    private static AdminDictionaryEntry ReadDictionary(SqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5), Utc(reader.GetDateTime(6)));

    private async ValueTask AuditAsync(string actor, string action, string type, string id, string outcome, string? detail, CancellationToken cancellationToken) =>
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, type, id, outcome, detail), cancellationToken);

    private static AuditEvent ReadAudit(SqlDataReader reader) => new(reader.GetGuid(0), Utc(reader.GetDateTime(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static void AddPolicyParameters(SqlCommand command, string key, string category, string en, string ar, string payload, string? secretRef, bool restart, bool enabled, DateTimeOffset updated)
    {
        command.Parameters.Add("@Key", SqlDbType.NVarChar, 160).Value = key;
        command.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category;
        command.Parameters.Add("@En", SqlDbType.NVarChar, 200).Value = en;
        command.Parameters.Add("@Ar", SqlDbType.NVarChar, 200).Value = ar;
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
        command.Parameters.Add("@SecretRef", SqlDbType.NVarChar, 300).Value = Db(secretRef);
        command.Parameters.Add("@Restart", SqlDbType.Bit).Value = restart;
        command.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
        command.Parameters.Add("@Updated", SqlDbType.DateTime2).Value = updated.UtcDateTime;
    }

    private static void AddUserParameters(SqlCommand command, Guid id, string userName, string displayName, string? externalSubject, bool enabled, DateTimeOffset now)
    {
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 200).Value = userName;
        command.Parameters.Add("@Display", SqlDbType.NVarChar, 300).Value = displayName;
        command.Parameters.Add("@External", SqlDbType.NVarChar, 200).Value = Db(externalSubject);
        command.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
    }

    private static void AddDictionaryParameters(SqlCommand command, string dictionaryKey, string entryKey, string en, string ar, bool enabled, DateTimeOffset now)
    {
        command.Parameters.Add("@D", SqlDbType.NVarChar, 120).Value = dictionaryKey;
        command.Parameters.Add("@E", SqlDbType.NVarChar, 120).Value = entryKey;
        command.Parameters.Add("@En", SqlDbType.NVarChar, 300).Value = en;
        command.Parameters.Add("@Ar", SqlDbType.NVarChar, 300).Value = ar;
        command.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
    }

    private static string? TryString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool TryInt(JsonElement element, string name, out int value) { value = 0; return element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out value); }
    private static bool TryBool(JsonElement element, string name, out bool value) { value = false; return element.TryGetProperty(name, out var item) && (item.ValueKind == JsonValueKind.True || item.ValueKind == JsonValueKind.False) && (value = item.GetBoolean()) == value; }
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';

    private sealed class UserAccumulator
    {
        public UserAccumulator(Guid id, string userName, string displayName, string? externalSubject, bool enabled, long version)
        { Id=id; UserName=userName; DisplayName=displayName; ExternalSubject=externalSubject; Enabled=enabled; Version=version; }
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
