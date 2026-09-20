using MAM.Application.Auditing;
using MAM.Application.Tapes;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Tapes;

public sealed class TapeManagementConfigurationStore : ITapeManagementConfigurationService
{
    private readonly SqlServerConnectionFactory? _sql;
    private readonly DemoSqliteDatabase? _demo;
    private readonly IAuditSink _audit;

    public TapeManagementConfigurationStore(SqlServerConnectionFactory? sql, DemoSqliteDatabase? demo, IAuditSink audit)
    {
        _sql=sql; _demo=demo; _audit=audit;
        if ((_sql is null)==(_demo is null))
            throw new InvalidOperationException("Tape management configuration requires exactly one authoritative store.");
    }

    public async Task<IReadOnlyList<TapeDepartmentItem>> ListDepartmentsAsync(bool includeInactive, CancellationToken cancellationToken=default)
    {
        if (_sql is not null)
        {
            await using var c=await _sql.OpenAsync(cancellationToken);
            await using var cmd=new SqlCommand("""
                SELECT Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy
                FROM dbo.inv_tape_departments
                WHERE @All=1 OR IsActive=1
                ORDER BY SortOrder,NameEn,Code;
                """,c){CommandTimeout=_sql.CommandTimeoutSeconds};
            cmd.Parameters.AddWithValue("@All",includeInactive);
            await using var r=await cmd.ExecuteReaderAsync(cancellationToken);
            var rows=new List<TapeDepartmentItem>();
            while(await r.ReadAsync(cancellationToken))
                rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.GetInt32(4),Utc(r.GetDateTime(5)),r.GetString(6)));
            return rows;
        }

        await using var dc=await OpenDemoAsync(cancellationToken);
        await using var dq=dc.CreateCommand();
        dq.CommandText="SELECT Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy FROM DemoTapeDepartment WHERE $all=1 OR IsActive=1 ORDER BY SortOrder,NameEn,Code;";
        dq.Parameters.AddWithValue("$all",includeInactive?1:0);
        await using var dr=await dq.ExecuteReaderAsync(cancellationToken);
        var demoRows=new List<TapeDepartmentItem>();
        while(await dr.ReadAsync(cancellationToken))
            demoRows.Add(new(dr.GetString(0),dr.GetString(1),dr.GetString(2),dr.GetInt32(3)!=0,dr.GetInt32(4),DemoSqliteDatabase.FromDb(dr.GetString(5)),dr.GetString(6)));
        return demoRows;
    }

    public async Task<TapeDepartmentItem> UpsertDepartmentAsync(UpsertTapeDepartmentRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code=Required(request.Code,256,"department_code_required");
        var en=Required(request.NameEn,256,"department_name_en_required");
        var ar=Required(request.NameAr,256,"department_name_ar_required");
        var actor=Actor(actorId);
        var now=DateTimeOffset.UtcNow;

        if (_sql is not null)
        {
            await using var c=await _sql.OpenAsync(cancellationToken);
            await using var cmd=new SqlCommand("""
                MERGE dbo.inv_tape_departments AS t
                USING (SELECT @Code AS Code) s ON t.Code=s.Code
                WHEN MATCHED THEN UPDATE SET NameEn=@En,NameAr=@Ar,IsActive=@Active,SortOrder=@Sort,UpdatedAtUtc=@Now,UpdatedBy=@Actor
                WHEN NOT MATCHED THEN INSERT(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy)
                    VALUES(@Code,@En,@Ar,@Active,@Sort,@Now,@Actor);
                """,c){CommandTimeout=_sql.CommandTimeoutSeconds};
            cmd.Parameters.AddWithValue("@Code",code);cmd.Parameters.AddWithValue("@En",en);cmd.Parameters.AddWithValue("@Ar",ar);
            cmd.Parameters.AddWithValue("@Active",request.IsActive);cmd.Parameters.AddWithValue("@Sort",request.SortOrder);
            cmd.Parameters.AddWithValue("@Now",now.UtcDateTime);cmd.Parameters.AddWithValue("@Actor",actor);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var c=await OpenDemoAsync(cancellationToken);
            await using var cmd=c.CreateCommand();
            cmd.CommandText="""
                INSERT INTO DemoTapeDepartment(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy)
                VALUES($code,$en,$ar,$active,$sort,$now,$actor)
                ON CONFLICT(Code) DO UPDATE SET NameEn=excluded.NameEn,NameAr=excluded.NameAr,IsActive=excluded.IsActive,
                    SortOrder=excluded.SortOrder,UpdatedAtUtc=excluded.UpdatedAtUtc,UpdatedBy=excluded.UpdatedBy;
                """;
            cmd.Parameters.AddWithValue("$code",code);cmd.Parameters.AddWithValue("$en",en);cmd.Parameters.AddWithValue("$ar",ar);
            cmd.Parameters.AddWithValue("$active",request.IsActive?1:0);cmd.Parameters.AddWithValue("$sort",request.SortOrder);
            cmd.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));cmd.Parameters.AddWithValue("$actor",actor);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var result=(await ListDepartmentsAsync(true,cancellationToken)).First(x=>string.Equals(x.Code,code,StringComparison.OrdinalIgnoreCase));
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actor,"tape-department.upsert","TapeDepartment",code,"Success",$"active={result.IsActive};sort={result.SortOrder}"),cancellationToken);
        return result;
    }

    public async Task<bool> IsActiveDepartmentAsync(string code,CancellationToken cancellationToken=default)
    {
        var normalized=Required(code,256,"department_code_required");
        return (await ListDepartmentsAsync(false,cancellationToken)).Any(x=>string.Equals(x.Code,normalized,StringComparison.OrdinalIgnoreCase));
    }

    public async Task RecordPrintEventAsync(TapeInventoryItem tape,TapePrintEventRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(tape);ArgumentNullException.ThrowIfNull(request);
        var kind=(request.Kind??string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("label" or "report"))
            throw new TapeInventoryRequestException("invalid_print_kind","Print kind must be label or report.");
        if (request.WidthMm is <=0 or >500 || request.HeightMm is <=0 or >500)
            throw new TapeInventoryRequestException("invalid_print_size","Print dimensions must be between 0 and 500 mm.");
        var actor=Actor(actorId);
        var detail=$"kind={kind};labelType={Safe(request.LabelType)};widthMm={request.WidthMm};heightMm={request.HeightMm};title={Safe(tape.Title)}";
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,
            kind=="label"?"tape.barcode.printed":"tape.report.printed","Tape",tape.TapeCode,"Success",detail),cancellationToken);
    }

    private async ValueTask<SqliteConnection> OpenDemoAsync(CancellationToken ct)
    {
        var c=await _demo!.OpenAsync(ct);
        await using var q=c.CreateCommand();q.CommandText=DemoSchema;await q.ExecuteNonQueryAsync(ct);
        return c;
    }

    private static string Required(string? value,int max,string code)
    {
        var v=value?.Trim()??string.Empty;
        if(v.Length==0)throw new TapeInventoryRequestException(code,"A required value is missing.");
        if(v.Length>max)throw new TapeInventoryRequestException("value_too_long",$"Value exceeds {max} characters.");
        return v;
    }
    private static string Actor(string? value){var v=string.IsNullOrWhiteSpace(value)?"unknown":value.Trim();return v[..Math.Min(v.Length,256)];}
    private static string Safe(string? value)=>(value??string.Empty).Replace(";"," ",StringComparison.Ordinal).Trim();
    private static DateTimeOffset Utc(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));

    private const string DemoSchema="""
        CREATE TABLE IF NOT EXISTS DemoTapeDepartment(
            Code TEXT PRIMARY KEY,NameEn TEXT NOT NULL,NameAr TEXT NOT NULL,IsActive INTEGER NOT NULL,SortOrder INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,UpdatedBy TEXT NOT NULL);
        INSERT OR IGNORE INTO DemoTapeDepartment VALUES('MEDIA','Media Department','الإدارة الإعلامية',1,10,'2000-01-01T00:00:00.0000000Z','seed');
        """;
}
