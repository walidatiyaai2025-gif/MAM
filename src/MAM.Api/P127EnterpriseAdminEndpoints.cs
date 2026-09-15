using System.Data;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using MAM.Application.Identity;
using MAM.Application.Storage;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P127EnterpriseAdminEndpoints
{
    private static readonly Guid UncategorizedCategoryId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Regex VersionSuffix = new(@"\s+Version\s+\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var admin = app.MapGroup($"{configuredApiBasePath}/v1/admin");

        admin.MapGet("/directory-search", async (string? query, CancellationToken cancellationToken) =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { error = "directory_search_unavailable", detail = "Active Directory search is available only on the Windows production host." }, statusCode: 503);

            var value = query?.Trim() ?? string.Empty;
            if (value.Length < 2 || value.Length > 200 || value.Any(char.IsControl))
                return Results.BadRequest(new { error = "invalid_directory_query", detail = "Enter at least two valid directory search characters." });

            try
            {
                var rows = await SearchDirectoryAsync(value, cancellationToken);
                return Results.Ok(rows);
            }
            catch (LdapException ex)
            {
                return Results.Json(new { error = "directory_search_unavailable", detail = $"Active Directory lookup failed with LDAP error {ex.ErrorCode}." }, statusCode: 503);
            }
            catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
            {
                return Results.Json(new { error = "directory_search_unavailable", detail = "Active Directory lookup could not be initialized." }, statusCode: 503);
            }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapGet("/users/page", async (
            int? page,
            int? pageSize,
            string? query,
            SqlServerConnectionFactory connections,
            CancellationToken cancellationToken) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 10, 1, 50);
            var offset = checked((currentPage - 1) * size);
            var term = query?.Trim();

            await using var connection = await connections.OpenAsync(cancellationToken);
            var where = string.IsNullOrWhiteSpace(term)
                ? string.Empty
                : "WHERE u.UserName LIKE @Query OR u.DisplayName LIKE @Query OR u.ExternalSubject LIKE @Query";

            long total;
            await using (var count = new SqlCommand($"SELECT COUNT_BIG(*) FROM dbo.MamUser u {where};", connection)
                         { CommandTimeout = connections.CommandTimeoutSeconds })
            {
                if (!string.IsNullOrWhiteSpace(term)) count.Parameters.Add("@Query", SqlDbType.NVarChar, 220).Value = $"%{term}%";
                total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
            }

            var users = new List<PagedAdminUser>();
            const string selectTail = """
                ORDER BY u.DisplayName,u.UserName,u.UserId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
            var sql = $"""
                SELECT u.UserId,u.UserName,u.DisplayName,u.ExternalSubject,u.IsEnabled,u.Version,
                       STRING_AGG(r.RoleName, N',') WITHIN GROUP (ORDER BY r.RoleName) Roles
                FROM dbo.MamUser u
                LEFT JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId
                LEFT JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
                {where}
                GROUP BY u.UserId,u.UserName,u.DisplayName,u.ExternalSubject,u.IsEnabled,u.Version
                {selectTail}
                """;
            await using (var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds })
            {
                if (!string.IsNullOrWhiteSpace(term)) command.Parameters.Add("@Query", SqlDbType.NVarChar, 220).Value = $"%{term}%";
                command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
                command.Parameters.Add("@PageSize", SqlDbType.Int).Value = size;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var roles = reader.IsDBNull(6)
                        ? Array.Empty<string>()
                        : reader.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    users.Add(new PagedAdminUser(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetBoolean(4), reader.GetInt64(5), roles));
                }
            }

            return Results.Ok(new { items = users, totalCount = total, page = currentPage, pageSize = size, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size)) });
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        admin.MapDelete("/users/{userId:guid}", async (
            Guid userId,
            ClaimsPrincipal principal,
            SqlServerConnectionFactory connections,
            IAuditSink audit,
            CancellationToken cancellationToken) =>
        {
            if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId) && currentUserId == userId)
                return Results.Conflict(new { error = "cannot_delete_current_user", detail = "The currently signed-in administrator cannot delete their own account." });

            await using var connection = await connections.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                string? userName = null;
                bool isAdministrator = false;
                await using (var read = new SqlCommand("""
                    SELECT u.UserName,CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.MamUserRole ur JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
                        WHERE ur.UserId=u.UserId AND r.RoleName=N'Administrator') THEN 1 ELSE 0 END
                    FROM dbo.MamUser u WHERE u.UserId=@Id;
                    """, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    read.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
                    await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        userName = reader.GetString(0);
                        isAdministrator = reader.GetBoolean(1);
                    }
                }
                if (userName is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.NotFound(new { error = "user_not_found", detail = "The MAM user no longer exists." });
                }

                if (isAdministrator)
                {
                    await using var countAdmins = new SqlCommand("""
                        SELECT COUNT_BIG(*) FROM dbo.MamUser u
                        JOIN dbo.MamUserRole ur ON ur.UserId=u.UserId
                        JOIN dbo.MamRole r ON r.RoleId=ur.RoleId
                        WHERE r.RoleName=N'Administrator' AND u.IsEnabled=1 AND u.UserId<>@Id;
                        """, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds };
                    countAdmins.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
                    if (Convert.ToInt64(await countAdmins.ExecuteScalarAsync(cancellationToken)) == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Conflict(new { error = "last_administrator", detail = "The last enabled MAM administrator cannot be deleted." });
                    }
                }

                await using (var assets = new SqlCommand("UPDATE dbo.MediaAsset SET CreatedByUserId=NULL WHERE CreatedByUserId=@Id; UPDATE dbo.MediaAsset SET UpdatedByUserId=NULL WHERE UpdatedByUserId=@Id;", connection, transaction)
                             { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    assets.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
                    await assets.ExecuteNonQueryAsync(cancellationToken);
                }
                await using (var roles = new SqlCommand("DELETE dbo.MamUserRole WHERE UserId=@Id;", connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    roles.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
                    await roles.ExecuteNonQueryAsync(cancellationToken);
                }
                await using (var user = new SqlCommand("DELETE dbo.MamUser WHERE UserId=@Id;", connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    user.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = userId;
                    if (await user.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException("User deletion did not affect exactly one row.");
                }
                await transaction.CommitAsync(cancellationToken);

                var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "administration.user.deleted", "MamUser", userId.ToString("D"), "Success", $"username={userName}"), cancellationToken);
                return Results.NoContent();
            }
            catch (SqlException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new { error = "user_delete_conflict", detail = $"The user is still referenced by protected system data (SQL {ex.Number})." });
            }
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);

        var uploads = app.MapGroup($"{configuredApiBasePath}/v1/uploads");
        uploads.MapPost("/duplicates/{existingAssetId:guid}/version", async (
            Guid existingAssetId,
            ClaimsPrincipal principal,
            SqlServerConnectionFactory connections,
            IStorageObjectStore primary,
            IDiscoveryService discovery,
            IAuditSink audit,
            MamSettings settings,
            CancellationToken cancellationToken) =>
        {
            var roles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string mediaKind;
            try { mediaKind = await discovery.GetAssetMediaKindAsync(existingAssetId, cancellationToken); }
            catch (DiscoveryRequestException ex) { return Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode); }
            if (!await discovery.IsMediaActionAllowedAsync(roles, mediaKind, "upload", cancellationToken))
                return Results.Json(new { error = "media_type_permission_denied", detail = $"The current role is not permitted to upload {mediaKind} media." }, statusCode: 403);

            ExistingOriginal? source = null;
            await using (var connection = await connections.OpenAsync(cancellationToken))
            {
                const string sql = """
                    SELECT a.Title,o.ObjectKey,o.OriginalFileName,o.Length,o.Sha256,
                           (SELECT COUNT_BIG(*) FROM dbo.MamMediaOriginal x WHERE x.Sha256=o.Sha256) VersionCount
                    FROM dbo.MediaAsset a JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId
                    WHERE a.AssetId=@AssetId;
                    """;
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
                command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = existingAssetId;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    source = new ExistingOriginal(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4).Trim(), reader.GetInt64(5));
            }
            if (source is null) return Results.NotFound(new { error = "original_not_found", detail = "The existing authoritative original was not found." });

            var newAssetId = Guid.NewGuid();
            var versionNumber = checked((int)Math.Min(int.MaxValue, source.VersionCount + 1));
            var baseTitle = VersionSuffix.Replace(source.Title, string.Empty).Trim();
            var suffix = $" Version {versionNumber}";
            if (baseTitle.Length + suffix.Length > 300) baseTitle = baseTitle[..Math.Max(1, 300 - suffix.Length)].TrimEnd();
            var title = baseTitle + suffix;
            var now = DateTimeOffset.UtcNow;
            var objectKey = BuildObjectKey(settings, newAssetId, source.OriginalFileName, now);

            StorageWriteResult write;
            await using (var original = await primary.OpenReadAsync(source.ObjectKey, cancellationToken))
                write = await primary.WriteAsync(objectKey, original, source.Sha256, cancellationToken);
            var verification = await primary.VerifyAsync(objectKey, source.Sha256, cancellationToken);
            if (!verification.Exists || !verification.ChecksumMatches || verification.Length != source.Length)
            {
                await primary.DeleteAsync(objectKey, CancellationToken.None);
                return Results.Json(new { error = "version_copy_verification_failed", detail = "The new version copy failed Primary Storage verification." }, statusCode: 503);
            }

            try
            {
                await using var connection = await connections.OpenAsync(cancellationToken);
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                var actorId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : (Guid?)null;
                await using (var asset = new SqlCommand("""
                    INSERT dbo.MediaAsset(AssetId,Title,Lifecycle,Version,CreatedAtUtc,UpdatedAtUtc,CreatedByUserId,UpdatedByUserId)
                    VALUES(@AssetId,@Title,0,1,@Now,@Now,@Actor,@Actor);
                    """, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    asset.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = newAssetId;
                    asset.Parameters.Add("@Title", SqlDbType.NVarChar, 300).Value = title;
                    asset.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
                    asset.Parameters.Add("@Actor", SqlDbType.UniqueIdentifier).Value = (object?)actorId ?? DBNull.Value;
                    await asset.ExecuteNonQueryAsync(cancellationToken);
                }
                await using (var original = new SqlCommand("""
                    INSERT dbo.MamMediaOriginal(AssetId,StorageTargetId,ObjectKey,OriginalFileName,Length,Sha256,VerifiedAtUtc)
                    VALUES(@AssetId,@Target,@ObjectKey,@FileName,@Length,@Sha,@Now);
                    """, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    original.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = newAssetId;
                    original.Parameters.Add("@Target", SqlDbType.NVarChar, 100).Value = primary.TargetId;
                    original.Parameters.Add("@ObjectKey", SqlDbType.NVarChar, 1024).Value = write.ObjectKey;
                    original.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = source.OriginalFileName;
                    original.Parameters.Add("@Length", SqlDbType.BigInt).Value = write.Length;
                    original.Parameters.Add("@Sha", SqlDbType.Char, 64).Value = write.Sha256;
                    original.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
                    await original.ExecuteNonQueryAsync(cancellationToken);
                }
                await using (var category = new SqlCommand("""
                    IF OBJECT_ID(N'dbo.MamAssetCategory',N'U') IS NOT NULL
                       AND EXISTS(SELECT 1 FROM dbo.MamCategory WHERE CategoryId=@CategoryId)
                    INSERT dbo.MamAssetCategory(AssetId,CategoryId,AssignedBy,AssignedAtUtc)
                    VALUES(@AssetId,@CategoryId,@ActorText,@Now);
                    """, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
                {
                    category.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = newAssetId;
                    category.Parameters.Add("@CategoryId", SqlDbType.UniqueIdentifier).Value = UncategorizedCategoryId;
                    category.Parameters.Add("@ActorText", SqlDbType.NVarChar, 200).Value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                    category.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
                    await category.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await primary.DeleteAsync(objectKey, CancellationToken.None);
                throw;
            }

            var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), now, actor, "upload.duplicate.version-created", "MediaAsset", newAssetId.ToString("D"), "Success", $"sourceAsset={existingAssetId:D};version={versionNumber};sha256={source.Sha256}"), cancellationToken);
            return Results.Ok(new { assetId = newAssetId, sourceAssetId = existingAssetId, title, version = versionNumber, originalFileName = source.OriginalFileName, sha256 = source.Sha256 });
        }).RequireAuthorization(MamSecurity.CatalogWritePolicy);
    }

    private static async Task<IReadOnlyList<DirectoryUserResult>> SearchDirectoryAsync(string query, CancellationToken cancellationToken)
    {
        var netbios = Env("MAM_AD_NETBIOS_DOMAIN", "DA");
        var dns = Env("MAM_AD_DNS_DOMAIN", "da.gov.kw");
        var server = Env("MAM_AD_LDAP_SERVER", dns);
        var identifier = new LdapDirectoryIdentifier(server, 389, false, false);
        using var connection = new LdapConnection(identifier, CredentialCache.DefaultNetworkCredentials, AuthType.Negotiate)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.Signing = true;
        connection.SessionOptions.Sealing = true;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        connection.Bind();

        var rootRequest = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
        var root = (SearchResponse)connection.SendRequest(rootRequest, TimeSpan.FromSeconds(10));
        cancellationToken.ThrowIfCancellationRequested();
        var baseDn = root.Entries.Count > 0 ? Attribute(root.Entries[0], "defaultNamingContext") : null;
        if (string.IsNullOrWhiteSpace(baseDn)) throw new InvalidOperationException("Active Directory default naming context was not returned.");

        var escaped = EscapeFilter(query);
        var filter = $"(&(objectCategory=person)(objectClass=user)(|(sAMAccountName=*{escaped}*)(userPrincipalName=*{escaped}*)(displayName=*{escaped}*)(mail=*{escaped}*)))";
        var request = new SearchRequest(baseDn, filter, SearchScope.Subtree,
            "objectGUID", "sAMAccountName", "userPrincipalName", "displayName", "mail", "userAccountControl", "msDS-User-Account-Control-Computed", "pwdLastSet");
        request.SizeLimit = 10;
        var response = (SearchResponse)connection.SendRequest(request, TimeSpan.FromSeconds(10));
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<DirectoryUserResult>();
        foreach (SearchResultEntry entry in response.Entries)
        {
            var sam = Attribute(entry, "sAMAccountName") ?? string.Empty;
            if (sam.Length == 0) continue;
            var upn = Attribute(entry, "userPrincipalName") ?? $"{sam}@{dns}";
            var display = Attribute(entry, "displayName") ?? sam;
            var mail = Attribute(entry, "mail") ?? upn;
            var uac = IntAttribute(entry, "userAccountControl");
            var computed = IntAttribute(entry, "msDS-User-Account-Control-Computed");
            var pwdLastSet = LongAttribute(entry, "pwdLastSet");
            result.Add(new DirectoryUserResult(
                sam, upn, display, mail, $"{netbios}\\{sam}",
                (uac & 0x2) != 0, (computed & 0x10) != 0, (computed & 0x800000) != 0, pwdLastSet == 0));
        }
        return result;
    }

    private static string BuildObjectKey(MamSettings settings, Guid assetId, string fileName, DateTimeOffset now)
    {
        var layout = settings.Storage.Primary.PathLayout
            .Replace("yyyy", now.Year.ToString("0000"), StringComparison.Ordinal)
            .Replace("MM", now.Month.ToString("00"), StringComparison.Ordinal)
            .Replace("{AssetId}", assetId.ToString("D"), StringComparison.Ordinal);
        return string.Join('/', new[] { settings.Storage.Primary.OriginalsPrefix.Trim('/'), layout.Trim('/'), fileName });
    }

    private static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } value ? value : fallback;
    private static string EscapeFilter(string value) => value.Replace("\\", "\\5c", StringComparison.Ordinal).Replace("*", "\\2a", StringComparison.Ordinal).Replace("(", "\\28", StringComparison.Ordinal).Replace(")", "\\29", StringComparison.Ordinal).Replace("\0", "\\00", StringComparison.Ordinal);
    private static string? Attribute(SearchResultEntry entry, string name) => entry.Attributes[name]?.Count > 0 ? entry.Attributes[name]![0]?.ToString() : null;
    private static int IntAttribute(SearchResultEntry entry, string name) => int.TryParse(Attribute(entry, name), out var value) ? value : 0;
    private static long LongAttribute(SearchResultEntry entry, string name) => long.TryParse(Attribute(entry, name), out var value) ? value : 0;

    private sealed record DirectoryUserResult(string SamAccountName, string UserPrincipalName, string DisplayName, string Mail, string ExternalSubject, bool Disabled, bool Locked, bool PasswordExpired, bool PasswordChangeRequired);
    private sealed record PagedAdminUser(Guid UserId, string UserName, string DisplayName, string? ExternalSubject, bool IsEnabled, long Version, IReadOnlyList<string> Roles);
    private sealed record ExistingOriginal(string Title, string ObjectKey, string OriginalFileName, long Length, string Sha256, long VersionCount);
}