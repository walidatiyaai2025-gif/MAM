using MAM.Application.Auditing;
using MAM.Application.Tapes;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Tapes;

public sealed class TapeAttachmentStore : ITapeAttachmentService
{
    private readonly SqlServerConnectionFactory? _sql;
    private readonly DemoSqliteDatabase? _demo;
    private readonly IAuditSink _audit;

    public TapeAttachmentStore(SqlServerConnectionFactory? sql, DemoSqliteDatabase? demo, IAuditSink audit)
    {
        _sql=sql;
        _demo=demo;
        _audit=audit;
        if((_sql is null)==(_demo is null))
            throw new InvalidOperationException("Tape attachments require exactly one authoritative store.");
    }

    public Task<IReadOnlyList<TapeAttachmentItem>> ListAsync(Guid tapeId,CancellationToken cancellationToken=default)
    {
        ValidTapeId(tapeId);
        return _sql is not null?ListSqlAsync(tapeId,cancellationToken):ListDemoAsync(tapeId,cancellationToken);
    }

    public Task<TapeAttachmentItem> LinkAsync(Guid tapeId,LinkTapeAttachmentRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        ValidTapeId(tapeId);
        ArgumentNullException.ThrowIfNull(request);
        if(request.AssetId==Guid.Empty)throw Bad("invalid_attachment_asset","Attachment asset id is required.");
        var actor=Actor(actorId);
        return _sql is not null?LinkSqlAsync(tapeId,request,actor,cancellationToken):LinkDemoAsync(tapeId,request,actor,cancellationToken);
    }

    public Task DeleteAsync(Guid tapeId,Guid attachmentId,string actorId,CancellationToken cancellationToken=default)
    {
        ValidTapeId(tapeId);
        if(attachmentId==Guid.Empty)throw Bad("invalid_attachment_id","Attachment id is required.");
        var actor=Actor(actorId);
        return _sql is not null?DeleteSqlAsync(tapeId,attachmentId,actor,cancellationToken):DeleteDemoAsync(tapeId,attachmentId,actor,cancellationToken);
    }

    private async Task<IReadOnlyList<TapeAttachmentItem>> ListSqlAsync(Guid tapeId,CancellationToken ct)
    {
        await using var c=await _sql!.OpenAsync(ct);
        await using var cmd=new SqlCommand("""
            SELECT ta.AttachmentId,ta.TapeId,ta.AssetId,ta.DisplayName,o.OriginalFileName,o.Length,tm.MediaType,
                   es.State,COALESCE(CONVERT(int,es.ProgressPercent),0),es.Detail,ta.CreatedAtUtc,ta.CreatedBy
            FROM dbo.inv_tape_attachments ta
            INNER JOIN dbo.MamMediaOriginal o ON o.AssetId=ta.AssetId
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=ta.AssetId
            LEFT JOIN dbo.MamTextExtractionStatus es ON es.AssetId=ta.AssetId AND es.ExtractionKind=N'ocr'
            WHERE ta.TapeId=@TapeId
            ORDER BY ta.CreatedAtUtc DESC,ta.AttachmentId;
            """,c){CommandTimeout=_sql.CommandTimeoutSeconds};
        cmd.Parameters.AddWithValue("@TapeId",tapeId);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<TapeAttachmentItem>();
        while(await r.ReadAsync(ct))rows.Add(ReadSql(r));
        return rows;
    }

    private async Task<TapeAttachmentItem> LinkSqlAsync(Guid tapeId,LinkTapeAttachmentRequest request,string actor,CancellationToken ct)
    {
        await using var c=await _sql!.OpenAsync(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(ct);

        await using(var tapeCmd=new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.inv_tapes WHERE TapeId=@TapeId;",c,tx))
        {
            tapeCmd.Parameters.AddWithValue("@TapeId",tapeId);
            if(Convert.ToInt64(await tapeCmd.ExecuteScalarAsync(ct))!=1)throw Bad("tape_not_found","Tape was not found.",404);
        }

        string fileName;
        long length;
        string? mediaKind;
        await using(var assetCmd=new SqlCommand("""
            SELECT o.OriginalFileName,o.Length,tm.MediaType
            FROM dbo.MediaAsset a
            INNER JOIN dbo.MamMediaOriginal o ON o.AssetId=a.AssetId
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=a.AssetId
            WHERE a.AssetId=@AssetId;
            """,c,tx))
        {
            assetCmd.Parameters.AddWithValue("@AssetId",request.AssetId);
            await using var r=await assetCmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw Bad("attachment_asset_not_found","Uploaded attachment asset/original was not found.",404);
            fileName=r.GetString(0);
            length=r.GetInt64(1);
            mediaKind=r.IsDBNull(2)?null:r.GetString(2);
        }

        EnsureSupported(fileName);
        var existing=await ReadLinkByAssetSqlAsync(c,tx,request.AssetId,ct);
        if(existing is not null)
        {
            if(existing.TapeId==tapeId){await tx.CommitAsync(ct);return existing;}
            throw Bad("attachment_already_linked","This uploaded file is already attached to another tape.",409);
        }

        var id=Guid.NewGuid();
        var now=DateTimeOffset.UtcNow;
        var display=DisplayName(request.DisplayName,fileName);
        await using(var cmd=new SqlCommand("""
            INSERT dbo.inv_tape_attachments(AttachmentId,TapeId,AssetId,DisplayName,CreatedAtUtc,CreatedBy)
            VALUES(@Id,@TapeId,@AssetId,@DisplayName,@Now,@Actor);
            """,c,tx))
        {
            cmd.Parameters.AddWithValue("@Id",id);
            cmd.Parameters.AddWithValue("@TapeId",tapeId);
            cmd.Parameters.AddWithValue("@AssetId",request.AssetId);
            cmd.Parameters.AddWithValue("@DisplayName",display);
            cmd.Parameters.AddWithValue("@Now",now.UtcDateTime);
            cmd.Parameters.AddWithValue("@Actor",actor);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);

        var item=new TapeAttachmentItem(id,tapeId,request.AssetId,display,fileName,length,mediaKind,null,0,null,now,actor);
        await AuditAsync(actor,"tape.attachment.created",item,$"tape={tapeId:D};asset={request.AssetId:D};file={fileName}",ct);
        return item;
    }

    private async Task<TapeAttachmentItem?> ReadLinkByAssetSqlAsync(SqlConnection c,SqlTransaction tx,Guid assetId,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT ta.AttachmentId,ta.TapeId,ta.AssetId,ta.DisplayName,o.OriginalFileName,o.Length,tm.MediaType,
                   es.State,COALESCE(CONVERT(int,es.ProgressPercent),0),es.Detail,ta.CreatedAtUtc,ta.CreatedBy
            FROM dbo.inv_tape_attachments ta
            INNER JOIN dbo.MamMediaOriginal o ON o.AssetId=ta.AssetId
            LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=ta.AssetId
            LEFT JOIN dbo.MamTextExtractionStatus es ON es.AssetId=ta.AssetId AND es.ExtractionKind=N'ocr'
            WHERE ta.AssetId=@AssetId;
            """,c,tx){CommandTimeout=_sql!.CommandTimeoutSeconds};
        cmd.Parameters.AddWithValue("@AssetId",assetId);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?ReadSql(r):null;
    }

    private async Task DeleteSqlAsync(Guid tapeId,Guid attachmentId,string actor,CancellationToken ct)
    {
        TapeAttachmentItem? item;
        await using(var c=await _sql!.OpenAsync(ct))
        {
            await using var find=new SqlCommand("""
                SELECT ta.AttachmentId,ta.TapeId,ta.AssetId,ta.DisplayName,o.OriginalFileName,o.Length,tm.MediaType,
                       es.State,COALESCE(CONVERT(int,es.ProgressPercent),0),es.Detail,ta.CreatedAtUtc,ta.CreatedBy
                FROM dbo.inv_tape_attachments ta
                INNER JOIN dbo.MamMediaOriginal o ON o.AssetId=ta.AssetId
                LEFT JOIN dbo.MamTechnicalMetadata tm ON tm.AssetId=ta.AssetId
                LEFT JOIN dbo.MamTextExtractionStatus es ON es.AssetId=ta.AssetId AND es.ExtractionKind=N'ocr'
                WHERE ta.TapeId=@TapeId AND ta.AttachmentId=@AttachmentId;
                """,c){CommandTimeout=_sql.CommandTimeoutSeconds};
            find.Parameters.AddWithValue("@TapeId",tapeId);find.Parameters.AddWithValue("@AttachmentId",attachmentId);
            await using(var r=await find.ExecuteReaderAsync(ct))item=await r.ReadAsync(ct)?ReadSql(r):null;
            if(item is null)throw Bad("attachment_not_found","Tape attachment was not found.",404);
            await using var del=new SqlCommand("DELETE dbo.inv_tape_attachments WHERE TapeId=@TapeId AND AttachmentId=@AttachmentId;",c){CommandTimeout=_sql.CommandTimeoutSeconds};
            del.Parameters.AddWithValue("@TapeId",tapeId);del.Parameters.AddWithValue("@AttachmentId",attachmentId);
            await del.ExecuteNonQueryAsync(ct);
        }
        await AuditAsync(actor,"tape.attachment.unlinked",item,$"tape={tapeId:D};asset={item.AssetId:D}",ct);
    }

    private async Task<IReadOnlyList<TapeAttachmentItem>> ListDemoAsync(Guid tapeId,CancellationToken ct)
    {
        await using var c=await OpenDemoAsync(ct);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
            SELECT ta.AttachmentId,ta.TapeId,ta.AssetId,ta.DisplayName,a.OriginalFileName,COALESCE(a.OriginalLength,0),a.MediaKind,
                   es.State,COALESCE(es.ProgressPercent,0),es.Detail,ta.CreatedAtUtc,ta.CreatedBy
            FROM DemoTapeAttachment ta
            INNER JOIN DemoAsset a ON a.AssetId=ta.AssetId
            LEFT JOIN DemoExtractionStatus es ON es.AssetId=ta.AssetId AND es.ExtractionKind='ocr'
            WHERE ta.TapeId=$tape ORDER BY ta.CreatedAtUtc DESC,ta.AttachmentId;
            """;
        cmd.Parameters.AddWithValue("$tape",tapeId.ToString("D"));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<TapeAttachmentItem>();
        while(await r.ReadAsync(ct))rows.Add(ReadDemo(r));
        return rows;
    }

    private async Task<TapeAttachmentItem> LinkDemoAsync(Guid tapeId,LinkTapeAttachmentRequest request,string actor,CancellationToken ct)
    {
        await using var c=await OpenDemoAsync(ct);
        await using(var tape=c.CreateCommand())
        {
            tape.CommandText="SELECT COUNT(*) FROM DemoTape WHERE TapeId=$id;";tape.Parameters.AddWithValue("$id",tapeId.ToString("D"));
            if(Convert.ToInt64(await tape.ExecuteScalarAsync(ct))!=1)throw Bad("tape_not_found","Tape was not found.",404);
        }

        string fileName;
        long length;
        string? mediaKind;
        await using(var asset=c.CreateCommand())
        {
            asset.CommandText="SELECT OriginalFileName,COALESCE(OriginalLength,0),MediaKind FROM DemoAsset WHERE AssetId=$id AND OriginalFileName IS NOT NULL;";
            asset.Parameters.AddWithValue("$id",request.AssetId.ToString("D"));
            await using var r=await asset.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw Bad("attachment_asset_not_found","Uploaded attachment asset/original was not found.",404);
            fileName=r.GetString(0);length=r.GetInt64(1);mediaKind=r.IsDBNull(2)?null:r.GetString(2);
        }
        EnsureSupported(fileName);

        var all=await ListAllDemoAsync(c,ct);
        var existing=all.FirstOrDefault(x=>x.AssetId==request.AssetId);
        if(existing is not null)
        {
            if(existing.TapeId==tapeId)return existing;
            throw Bad("attachment_already_linked","This uploaded file is already attached to another tape.",409);
        }

        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var display=DisplayName(request.DisplayName,fileName);
        await using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="INSERT INTO DemoTapeAttachment(AttachmentId,TapeId,AssetId,DisplayName,CreatedAtUtc,CreatedBy) VALUES($id,$tape,$asset,$name,$now,$actor);";
            cmd.Parameters.AddWithValue("$id",id.ToString("D"));cmd.Parameters.AddWithValue("$tape",tapeId.ToString("D"));
            cmd.Parameters.AddWithValue("$asset",request.AssetId.ToString("D"));cmd.Parameters.AddWithValue("$name",display);
            cmd.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));cmd.Parameters.AddWithValue("$actor",actor);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var item=new TapeAttachmentItem(id,tapeId,request.AssetId,display,fileName,length,mediaKind,null,0,null,now,actor);
        await AuditAsync(actor,"tape.attachment.created",item,$"tape={tapeId:D};asset={request.AssetId:D};file={fileName}",ct);
        return item;
    }

    private async Task<IReadOnlyList<TapeAttachmentItem>> ListAllDemoAsync(SqliteConnection c,CancellationToken ct)
    {
        await using var cmd=c.CreateCommand();
        cmd.CommandText="""
            SELECT ta.AttachmentId,ta.TapeId,ta.AssetId,ta.DisplayName,a.OriginalFileName,COALESCE(a.OriginalLength,0),a.MediaKind,
                   es.State,COALESCE(es.ProgressPercent,0),es.Detail,ta.CreatedAtUtc,ta.CreatedBy
            FROM DemoTapeAttachment ta INNER JOIN DemoAsset a ON a.AssetId=ta.AssetId
            LEFT JOIN DemoExtractionStatus es ON es.AssetId=ta.AssetId AND es.ExtractionKind='ocr';
            """;
        await using var r=await cmd.ExecuteReaderAsync(ct);var rows=new List<TapeAttachmentItem>();
        while(await r.ReadAsync(ct))rows.Add(ReadDemo(r));return rows;
    }

    private async Task DeleteDemoAsync(Guid tapeId,Guid attachmentId,string actor,CancellationToken ct)
    {
        await using var c=await OpenDemoAsync(ct);
        var item=(await ListAllDemoAsync(c,ct)).FirstOrDefault(x=>x.TapeId==tapeId&&x.AttachmentId==attachmentId)
            ?? throw Bad("attachment_not_found","Tape attachment was not found.",404);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="DELETE FROM DemoTapeAttachment WHERE TapeId=$tape AND AttachmentId=$id;";
        cmd.Parameters.AddWithValue("$tape",tapeId.ToString("D"));cmd.Parameters.AddWithValue("$id",attachmentId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(ct);
        await AuditAsync(actor,"tape.attachment.unlinked",item,$"tape={tapeId:D};asset={item.AssetId:D}",ct);
    }

    private async ValueTask<SqliteConnection> OpenDemoAsync(CancellationToken ct)
    {
        var c=await _demo!.OpenAsync(ct);
        await using var q=c.CreateCommand();q.CommandText=DemoSchema;await q.ExecuteNonQueryAsync(ct);
        return c;
    }

    private static TapeAttachmentItem ReadSql(SqlDataReader r)=>new(
        r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetInt64(5),
        r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetInt32(8),
        r.IsDBNull(9)?null:r.GetString(9),Utc(r.GetDateTime(10)),r.GetString(11));

    private static TapeAttachmentItem ReadDemo(SqliteDataReader r)=>new(
        Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),Guid.Parse(r.GetString(2)),r.GetString(3),r.GetString(4),r.GetInt64(5),
        r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetInt32(8),
        r.IsDBNull(9)?null:r.GetString(9),DemoSqliteDatabase.FromDb(r.GetString(10)),r.GetString(11));

    private static void ValidTapeId(Guid id){if(id==Guid.Empty)throw Bad("invalid_tape_id","Tape id is required.");}
    private static void EnsureSupported(string fileName)
    {
        if(!TapeAttachmentPolicy.SupportsOcr(fileName))
            throw Bad("attachment_ocr_type_not_supported",$"Tape attachment type '{Path.GetExtension(fileName)}' is not supported for OCR.",415);
    }
    private static string DisplayName(string? requested,string fallback)
    {
        var value=string.IsNullOrWhiteSpace(requested)?fallback:requested.Trim();
        if(value.Length>512)throw Bad("attachment_name_too_long","Attachment display name must be 512 characters or fewer.");
        return value;
    }
    private static string Actor(string? value){var v=string.IsNullOrWhiteSpace(value)?"unknown":value.Trim();return v[..Math.Min(v.Length,256)];}
    private static DateTimeOffset Utc(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));
    private static TapeInventoryRequestException Bad(string code,string message,int status=400)=>new(code,message,status);
    private ValueTask AuditAsync(string actor,string action,TapeAttachmentItem item,string detail,CancellationToken ct)=>
        _audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,action,"TapeAttachment",item.AttachmentId.ToString("D"),"Success",detail),ct);

    private const string DemoSchema="""
        CREATE TABLE IF NOT EXISTS DemoTapeAttachment(
            AttachmentId TEXT PRIMARY KEY,
            TapeId TEXT NOT NULL,
            AssetId TEXT NOT NULL UNIQUE,
            DisplayName TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            FOREIGN KEY(TapeId) REFERENCES DemoTape(TapeId) ON DELETE CASCADE,
            FOREIGN KEY(AssetId) REFERENCES DemoAsset(AssetId) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_DemoTapeAttachment_Tape ON DemoTapeAttachment(TapeId,CreatedAtUtc DESC);
        """;
}
