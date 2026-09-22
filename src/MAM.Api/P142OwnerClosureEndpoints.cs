using System.Data;
using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P142OwnerClosureEndpoints
{
    private static readonly string[] ManagedPermissions =
    [
        MamPermissions.CatalogRead,
        MamPermissions.CatalogWrite,
        MamPermissions.CatalogDelete,
        MamPermissions.AuditRead,
        MamPermissions.Administration,
        MamPermissions.TapeView,
        MamPermissions.TapeCreate,
        MamPermissions.TapeEdit,
        MamPermissions.TapeDelete,
        MamPermissions.TapePrint,
        MamPermissions.TapeSearch,
        MamPermissions.TapeManageFormats,
        MamPermissions.TapeManageDepartments,
        MamPermissions.SystemFunctionsView,
        MamPermissions.SystemFunctionsManage
    ];

    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var group = app.MapGroup($"{configuredApiBasePath}/v1/admin/references")
            .RequireAuthorization(MamSecurity.AdministrationPolicy);

        group.MapGet("", async (SqlServerConnectionFactory connections, CancellationToken ct) =>
        {
            await using var connection = await connections.OpenAsync(ct);
            const string sql = """
                SELECT s.SubjectId,s.NameEn,s.NameAr,s.DescriptionEn,s.DescriptionAr,s.TagsText,s.IsActive,s.CreatedAtUtc,s.UpdatedAtUtc,
                       (SELECT COUNT_BIG(*) FROM dbo.MamReferenceImage i WHERE i.SubjectId=s.SubjectId) ImageCount,
                       (SELECT COUNT_BIG(*) FROM dbo.MamAssetReferenceTag t WHERE t.SubjectId=s.SubjectId) TaggedAssetCount
                FROM dbo.MamReferenceSubject s
                ORDER BY s.IsActive DESC,s.NameEn,s.SubjectId;
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<ReferenceAdminSnapshot>();
            while (await reader.ReadAsync(ct))
                rows.Add(new ReferenceAdminSnapshot(
                    reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2)?null:reader.GetString(2),
                    reader.IsDBNull(3)?null:reader.GetString(3), reader.IsDBNull(4)?null:reader.GetString(4),
                    reader.IsDBNull(5)?null:reader.GetString(5), reader.GetBoolean(6),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7),DateTimeKind.Utc)),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8),DateTimeKind.Utc)),
                    reader.GetInt64(9), reader.GetInt64(10)));
            return Results.Ok(rows);
        });

        group.MapPost("", async (ReferenceAdminUpdateRequest request, ClaimsPrincipal principal, SqlServerConnectionFactory connections, IAuditSink audit, CancellationToken ct) =>
        {
            var validation = Validate(request); if (validation is not null) return validation;
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using var connection = await connections.OpenAsync(ct);
            const string sql = """
                INSERT dbo.MamReferenceSubject(SubjectId,NameEn,NameAr,DescriptionEn,DescriptionAr,TagsText,IsActive,CreatedAtUtc,UpdatedAtUtc)
                VALUES(@Id,@NameEn,@NameAr,@DescriptionEn,@DescriptionAr,@TagsText,@IsActive,@Now,@Now);
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
            Add(command,id,request,now);
            await command.ExecuteNonQueryAsync(ct);
            await Audit(audit,principal,"reference.created",id,request.NameEn,ct);
            return Results.Created($"{configuredApiBasePath}/v1/admin/references/{id:D}", new { subjectId=id });
        });

        group.MapPut("/{subjectId:guid}", async (Guid subjectId, ReferenceAdminUpdateRequest request, ClaimsPrincipal principal, SqlServerConnectionFactory connections, IAuditSink audit, CancellationToken ct) =>
        {
            var validation = Validate(request); if (validation is not null) return validation;
            var now = DateTime.UtcNow;
            await using var connection = await connections.OpenAsync(ct);
            const string sql = """
                UPDATE dbo.MamReferenceSubject
                SET NameEn=@NameEn,NameAr=@NameAr,DescriptionEn=@DescriptionEn,DescriptionAr=@DescriptionAr,TagsText=@TagsText,IsActive=@IsActive,UpdatedAtUtc=@Now
                WHERE SubjectId=@Id;
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
            Add(command,subjectId,request,now);
            if (await command.ExecuteNonQueryAsync(ct) != 1) return Results.NotFound(new { error="reference_not_found", detail="Reference subject was not found." });
            await Audit(audit,principal,"reference.updated",subjectId,request.NameEn,ct);
            return Results.Ok(new { subjectId });
        });

        group.MapDelete("/{subjectId:guid}", async (Guid subjectId, ClaimsPrincipal principal, SqlServerConnectionFactory connections, IAuditSink audit, CancellationToken ct) =>
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                string? name = null;
                await using (var read = new SqlCommand("SELECT NameEn FROM dbo.MamReferenceSubject WHERE SubjectId=@Id;", connection, transaction))
                {
                    read.Parameters.Add("@Id",SqlDbType.UniqueIdentifier).Value=subjectId;
                    name = Convert.ToString(await read.ExecuteScalarAsync(ct));
                }
                if (string.IsNullOrWhiteSpace(name)) { await transaction.RollbackAsync(ct); return Results.NotFound(new { error="reference_not_found", detail="Reference subject was not found." }); }
                foreach (var sql in new[]{
                    "DELETE dbo.MamAssetReferenceTag WHERE SubjectId=@Id;",
                    "DELETE dbo.MamReferenceImage WHERE SubjectId=@Id;",
                    "DELETE dbo.MamReferenceSubject WHERE SubjectId=@Id;"})
                {
                    await using var command = new SqlCommand(sql,connection,transaction);
                    command.Parameters.Add("@Id",SqlDbType.UniqueIdentifier).Value=subjectId;
                    await command.ExecuteNonQueryAsync(ct);
                }
                await transaction.CommitAsync(ct);
                await Audit(audit,principal,"reference.deleted",subjectId,name,ct);
                return Results.NoContent();
            }
            catch { try { await transaction.RollbackAsync(CancellationToken.None); } catch { } throw; }
        });


        var rolePermissions = app.MapGroup($"{configuredApiBasePath}/v1/admin/role-permissions")
            .RequireAuthorization(MamSecurity.AdministrationPolicy);

        rolePermissions.MapGet("", async (SqlServerConnectionFactory connections, CancellationToken ct) =>
        {
            await using var connection = await connections.OpenAsync(ct);
            try
            {
                const string sql = """
                    SELECT rp.RoleName,rp.PermissionKey,rp.IsAllowed,rp.UpdatedAtUtc,rp.UpdatedBy
                    FROM dbo.MamRolePermission rp
                    JOIN dbo.MamRole r ON r.RoleName=rp.RoleName
                    ORDER BY CASE rp.RoleName
                        WHEN N'Administrator' THEN 0
                        WHEN N'CatalogManager' THEN 1
                        WHEN N'CatalogEditor' THEN 2
                        WHEN N'Viewer' THEN 3
                        WHEN N'TapeManager' THEN 4
                        WHEN N'TapeOperator' THEN 5
                        WHEN N'TapeViewer' THEN 6
                        ELSE 99 END,
                        rp.PermissionKey;
                    """;
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
                await using var reader = await command.ExecuteReaderAsync(ct);
                var rows = new List<RolePermissionSnapshot>();
                while (await reader.ReadAsync(ct))
                    rows.Add(new RolePermissionSnapshot(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetBoolean(2),
                        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3),DateTimeKind.Utc)),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                return Results.Ok(rows);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return Results.Json(new
                {
                    error="role_permission_matrix_unavailable",
                    detail="Role permission matrix migration is not applied."
                }, statusCode:StatusCodes.Status503ServiceUnavailable);
            }
        });

        rolePermissions.MapPut("", async (
            RolePermissionUpdateRequest request,
            ClaimsPrincipal principal,
            SqlServerConnectionFactory connections,
            IAuditSink audit,
            CancellationToken ct) =>
        {
            var role = request.RoleName?.Trim() ?? string.Empty;
            var permission = request.PermissionKey?.Trim() ?? string.Empty;
            if (role.Length is 0 or > 100)
                return Results.BadRequest(new { error="invalid_role", detail="Role name is required." });
            if (!ManagedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error="invalid_permission", detail="Permission key is not managed by this matrix." });
            if (role.Equals(MamRoles.Administrator,StringComparison.OrdinalIgnoreCase)
                && permission.Equals(MamPermissions.Administration,StringComparison.OrdinalIgnoreCase)
                && !request.IsAllowed)
                return Results.BadRequest(new { error="administrator_lockout_blocked", detail="Administrator must retain administration.manage." });

            var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;
            await using var connection = await connections.OpenAsync(ct);

            await using (var exists = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.MamRole WHERE RoleName=@Role;",connection)
            { CommandTimeout=connections.CommandTimeoutSeconds })
            {
                exists.Parameters.Add("@Role",SqlDbType.NVarChar,100).Value=role;
                if (Convert.ToInt64(await exists.ExecuteScalarAsync(ct)) != 1)
                    return Results.NotFound(new { error="role_not_found", detail="Role was not found." });
            }

            const string sql = """
                MERGE dbo.MamRolePermission AS target
                USING (SELECT @Role RoleName,@Permission PermissionKey) AS source
                  ON target.RoleName=source.RoleName AND target.PermissionKey=source.PermissionKey
                WHEN MATCHED THEN
                  UPDATE SET IsAllowed=@Allowed,UpdatedAtUtc=@Now,UpdatedBy=@Actor
                WHEN NOT MATCHED THEN
                  INSERT(RoleName,PermissionKey,IsAllowed,UpdatedAtUtc,UpdatedBy)
                  VALUES(@Role,@Permission,@Allowed,@Now,@Actor);
                """;
            await using var command = new SqlCommand(sql,connection) { CommandTimeout=connections.CommandTimeoutSeconds };
            command.Parameters.Add("@Role",SqlDbType.NVarChar,100).Value=role;
            command.Parameters.Add("@Permission",SqlDbType.NVarChar,120).Value=permission;
            command.Parameters.Add("@Allowed",SqlDbType.Bit).Value=request.IsAllowed;
            command.Parameters.Add("@Now",SqlDbType.DateTime2).Value=now;
            command.Parameters.Add("@Actor",SqlDbType.NVarChar,200).Value=actor[..Math.Min(actor.Length,200)];
            await command.ExecuteNonQueryAsync(ct);

            await audit.AppendAsync(new AuditEvent(
                Guid.NewGuid(),DateTimeOffset.UtcNow,actor,"administration.role-permission.updated",
                "MamRolePermission",$"{role}:{permission}","Success",$"allowed={request.IsAllowed}"),ct);

            return Results.Ok(new RolePermissionSnapshot(role,permission,request.IsAllowed,
                new DateTimeOffset(DateTime.SpecifyKind(now,DateTimeKind.Utc)),actor));
        });
    }

    private static IResult? Validate(ReferenceAdminUpdateRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.NameEn) || r.NameEn.Trim().Length > 200)
            return Results.BadRequest(new { error="invalid_reference_name", detail="English reference name is required and must not exceed 200 characters." });
        if ((r.NameAr?.Trim().Length ?? 0) > 200) return Results.BadRequest(new { error="invalid_reference_name_ar", detail="Arabic reference name must not exceed 200 characters." });
        if ((r.DescriptionEn?.Length ?? 0) > 1000 || (r.DescriptionAr?.Length ?? 0) > 1000 || (r.TagsText?.Length ?? 0) > 1000)
            return Results.BadRequest(new { error="invalid_reference_text", detail="Reference description/tags exceed the allowed length." });
        return null;
    }

    private static void Add(SqlCommand c, Guid id, ReferenceAdminUpdateRequest r, DateTime now)
    {
        c.Parameters.Add("@Id",SqlDbType.UniqueIdentifier).Value=id;
        c.Parameters.Add("@NameEn",SqlDbType.NVarChar,200).Value=r.NameEn.Trim();
        c.Parameters.Add("@NameAr",SqlDbType.NVarChar,200).Value=(object?)Trim(r.NameAr)??DBNull.Value;
        c.Parameters.Add("@DescriptionEn",SqlDbType.NVarChar,1000).Value=(object?)Trim(r.DescriptionEn)??DBNull.Value;
        c.Parameters.Add("@DescriptionAr",SqlDbType.NVarChar,1000).Value=(object?)Trim(r.DescriptionAr)??DBNull.Value;
        c.Parameters.Add("@TagsText",SqlDbType.NVarChar,1000).Value=(object?)Trim(r.TagsText)??DBNull.Value;
        c.Parameters.Add("@IsActive",SqlDbType.Bit).Value=r.IsActive;
        c.Parameters.Add("@Now",SqlDbType.DateTime2).Value=now;
    }
    private static string? Trim(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
    private static ValueTask Audit(IAuditSink audit, ClaimsPrincipal p, string action, Guid id, string detail, CancellationToken ct)
        => audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,p.FindFirstValue(ClaimTypes.NameIdentifier)??p.Identity?.Name??"unknown",action,"MamReferenceSubject",id.ToString("D"),"Success",detail),ct);
    public sealed record RolePermissionUpdateRequest(string RoleName,string PermissionKey,bool IsAllowed);
    public sealed record RolePermissionSnapshot(string RoleName,string PermissionKey,bool IsAllowed,DateTimeOffset UpdatedAtUtc,string? UpdatedBy);
    public sealed record ReferenceAdminUpdateRequest(string NameEn,string? NameAr,string? DescriptionEn,string? DescriptionAr,string? TagsText,bool IsActive);
    public sealed record ReferenceAdminSnapshot(Guid SubjectId,string NameEn,string? NameAr,string? DescriptionEn,string? DescriptionAr,string? TagsText,bool IsActive,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc,long ImageCount,long TaggedAssetCount);
}
