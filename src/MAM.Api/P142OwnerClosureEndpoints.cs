using System.Data;
using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P142OwnerClosureEndpoints
{
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
    public sealed record ReferenceAdminUpdateRequest(string NameEn,string? NameAr,string? DescriptionEn,string? DescriptionAr,string? TagsText,bool IsActive);
    public sealed record ReferenceAdminSnapshot(Guid SubjectId,string NameEn,string? NameAr,string? DescriptionEn,string? DescriptionAr,string? TagsText,bool IsActive,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc,long ImageCount,long TaggedAssetCount);
}
