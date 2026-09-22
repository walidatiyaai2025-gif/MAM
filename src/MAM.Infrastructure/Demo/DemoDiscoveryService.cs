using System.Text.Json;
using MAM.Application.Auditing;
using MAM.Application.Discovery;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Demo;

public sealed class DemoDiscoveryService(DemoSqliteDatabase database, IAuditSink audit) : IDiscoveryService
{
    private static readonly Guid UncategorizedId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task<DiscoveryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await database.EnsureInitializedAsync(cancellationToken);
        return new DiscoveryHealth(true, "SqliteDemo", "Offline demo categories and indexed text search are ready.");
    }

    public async Task<IReadOnlyList<CategorySnapshot>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand();
        q.CommandText="""
            SELECT x.CategoryId,x.ParentCategoryId,x.NameEn,x.NameAr,x.IsSystem,x.SortOrder,x.Version,
                   (SELECT COUNT(*) FROM DemoAssetCategory ac WHERE ac.CategoryId=x.CategoryId),
                   (SELECT COUNT(*) FROM DemoCategory ch WHERE ch.ParentCategoryId=x.CategoryId),x.CreatedAtUtc,x.UpdatedAtUtc
            FROM DemoCategory x ORDER BY x.SortOrder,x.NameEn;
            """;
        await using var r=await q.ExecuteReaderAsync(cancellationToken); var rows=new List<CategorySnapshot>(); while(await r.ReadAsync(cancellationToken)) rows.Add(ReadCategory(r)); return rows;
    }

    public async Task<CategorySnapshot> CreateCategoryAsync(CreateCategoryRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateName(request.NameEn); if(request.ParentCategoryId is Guid parent) await EnsureCategoryAsync(parent,cancellationToken);
        var id=Guid.NewGuid(); var now=DateTimeOffset.UtcNow; await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand();
        q.CommandText="INSERT INTO DemoCategory(CategoryId,ParentCategoryId,NameEn,NameAr,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$parent,$en,$ar,0,$sort,1,$now,$now);";
        q.Parameters.AddWithValue("$id",id.ToString("D"));q.Parameters.AddWithValue("$parent",request.ParentCategoryId is Guid p?p.ToString("D"):DBNull.Value);q.Parameters.AddWithValue("$en",request.NameEn.Trim());q.Parameters.AddWithValue("$ar",Db(request.NameAr));q.Parameters.AddWithValue("$sort",request.SortOrder);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"discovery.category.created","Category",id.ToString("D"),cancellationToken);return (await ListCategoriesAsync(cancellationToken)).First(x=>x.CategoryId==id);
    }

    public async Task<CategorySnapshot> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if(categoryId==UncategorizedId) throw new DiscoveryRequestException("system_category_read_only","The Uncategorized system category cannot be edited.",409); ValidateName(request.NameEn); if(request.ParentCategoryId==categoryId) throw new DiscoveryRequestException("category_cycle","A category cannot be its own parent."); if(request.ParentCategoryId is Guid parent){await EnsureCategoryAsync(parent,cancellationToken); if(await IsDescendantAsync(parent,categoryId,cancellationToken))throw new DiscoveryRequestException("category_cycle","Category hierarchy cannot contain a cycle.",409);}
        var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="UPDATE DemoCategory SET ParentCategoryId=$parent,NameEn=$en,NameAr=$ar,SortOrder=$sort,Version=Version+1,UpdatedAtUtc=$now WHERE CategoryId=$id AND Version=$version AND IsSystem=0;";q.Parameters.AddWithValue("$parent",request.ParentCategoryId is Guid p?p.ToString("D"):DBNull.Value);q.Parameters.AddWithValue("$en",request.NameEn.Trim());q.Parameters.AddWithValue("$ar",Db(request.NameAr));q.Parameters.AddWithValue("$sort",request.SortOrder);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));q.Parameters.AddWithValue("$id",categoryId.ToString("D"));q.Parameters.AddWithValue("$version",request.ExpectedVersion);if(await q.ExecuteNonQueryAsync(cancellationToken)!=1){var current=(await ListCategoriesAsync(cancellationToken)).FirstOrDefault(x=>x.CategoryId==categoryId);if(current is null)throw new DiscoveryRequestException("category_not_found","Category was not found.",404);throw new DiscoveryRequestException("version_conflict","Category changed. Refresh and retry.",409);}await AuditAsync(actorId,"discovery.category.updated","Category",categoryId.ToString("D"),cancellationToken);return (await ListCategoriesAsync(cancellationToken)).First(x=>x.CategoryId==categoryId);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, string actorId, CancellationToken cancellationToken = default)
    {
        if(categoryId==UncategorizedId)throw new DiscoveryRequestException("system_category_read_only","The Uncategorized system category cannot be deleted.",409);
        await using var c=await database.OpenAsync(cancellationToken);await using var tx=await c.BeginTransactionAsync(cancellationToken);
        await using(var check=c.CreateCommand())
        {
            check.Transaction=(SqliteTransaction)tx;
            check.CommandText="SELECT (SELECT COUNT(*) FROM DemoCategory WHERE ParentCategoryId=$id)+(SELECT COUNT(*) FROM DemoAssetCategory WHERE CategoryId=$id);";
            check.Parameters.AddWithValue("$id",categoryId.ToString("D"));
            if(Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken))>0)
                throw new DiscoveryRequestException("category_not_empty","Move child categories and assets before deleting this category.",409);
        }
        await using(var del=c.CreateCommand()){del.Transaction=(SqliteTransaction)tx;del.CommandText="DELETE FROM DemoCategory WHERE CategoryId=$id AND IsSystem=0;";del.Parameters.AddWithValue("$id",categoryId.ToString("D"));if(await del.ExecuteNonQueryAsync(cancellationToken)!=1)throw new DiscoveryRequestException("category_not_found","Category was not found.",404);}
        await tx.CommitAsync(cancellationToken);await AuditAsync(actorId,"discovery.category.deleted","Category",categoryId.ToString("D"),cancellationToken);
    }

    public async Task<AssetCategorySnapshot> GetAssetCategoryAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken); var categoryId=await AssetCategoryIdAsync(assetId,cancellationToken)??UncategorizedId; var category=(await ListCategoriesAsync(cancellationToken)).First(x=>x.CategoryId==categoryId);return new AssetCategorySnapshot(assetId,category);
    }

    public async Task<AssetCategorySnapshot> AssignAssetCategoryAsync(Guid assetId, Guid? categoryId, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken);
        var target=categoryId??UncategorizedId;
        await EnsureCategoryAsync(target,cancellationToken);
        var category=(await ListCategoriesAsync(cancellationToken)).First(x=>x.CategoryId==target);
        await using var c=await database.OpenAsync(cancellationToken);
        await using var tx=await c.BeginTransactionAsync(cancellationToken);
        await using(var q=c.CreateCommand())
        {
            q.Transaction=(SqliteTransaction)tx;
            q.CommandText="INSERT INTO DemoAssetCategory(AssetId,CategoryId) VALUES($asset,$category) ON CONFLICT(AssetId) DO UPDATE SET CategoryId=excluded.CategoryId;";
            q.Parameters.AddWithValue("$asset",assetId.ToString("D"));
            q.Parameters.AddWithValue("$category",target.ToString("D"));
            await q.ExecuteNonQueryAsync(cancellationToken);
        }
        await using(var q=c.CreateCommand())
        {
            q.Transaction=(SqliteTransaction)tx;
            q.CommandText="UPDATE DemoAsset SET Category=$category,UpdatedAtUtc=$now WHERE AssetId=$asset;";
            q.Parameters.AddWithValue("$asset",assetId.ToString("D"));
            q.Parameters.AddWithValue("$category",category.NameEn);
            q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));
            await q.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        await AuditAsync(actorId,"discovery.asset.category-assigned","MediaAsset",assetId.ToString("D"),cancellationToken);
        return await GetAssetCategoryAsync(assetId,cancellationToken);
    }

    public async Task UpsertTextAsync(Guid assetId, string sourceKind, string? language, string text, string? contentSha256, IReadOnlyList<TextSegmentSnapshot>? segments, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken); if(string.IsNullOrWhiteSpace(sourceKind))throw new DiscoveryRequestException("source_required","Text source kind is required.");var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoAssetText(AssetId,SourceKind,Language,TextValue,ContentSha256,SegmentsJson,UpdatedAtUtc) VALUES($asset,$source,$language,$text,$sha,$segments,$now) ON CONFLICT(AssetId,SourceKind) DO UPDATE SET Language=excluded.Language,TextValue=excluded.TextValue,ContentSha256=excluded.ContentSha256,SegmentsJson=excluded.SegmentsJson,UpdatedAtUtc=excluded.UpdatedAtUtc;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));q.Parameters.AddWithValue("$source",sourceKind.Trim().ToLowerInvariant());q.Parameters.AddWithValue("$language",Db(language));q.Parameters.AddWithValue("$text",text??"");q.Parameters.AddWithValue("$sha",Db(contentSha256));q.Parameters.AddWithValue("$segments",JsonSerializer.Serialize(segments??[]));q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AssetTextSnapshot?> GetTextAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT Language,TextValue,ContentSha256,SegmentsJson,UpdatedAtUtc FROM DemoAssetText WHERE AssetId=$asset AND SourceKind=$source;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));q.Parameters.AddWithValue("$source",sourceKind.Trim().ToLowerInvariant());await using var r=await q.ExecuteReaderAsync(cancellationToken);if(!await r.ReadAsync(cancellationToken))return null;return new AssetTextSnapshot(assetId,sourceKind.Trim().ToLowerInvariant(),r.IsDBNull(0)?null:r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),Segments(r.GetString(3)),DemoSqliteDatabase.FromDb(r.GetString(4)));
    }

    public async Task SetExtractionStatusAsync(Guid assetId, string extractionKind, string state, int progressPercent, string? detail, bool completed, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken);var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoExtractionStatus(AssetId,ExtractionKind,State,ProgressPercent,Detail,UpdatedAtUtc,CompletedAtUtc) VALUES($asset,$kind,$state,$progress,$detail,$now,$completed) ON CONFLICT(AssetId,ExtractionKind) DO UPDATE SET State=excluded.State,ProgressPercent=excluded.ProgressPercent,Detail=excluded.Detail,UpdatedAtUtc=excluded.UpdatedAtUtc,CompletedAtUtc=excluded.CompletedAtUtc;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));q.Parameters.AddWithValue("$kind",extractionKind.Trim().ToLowerInvariant());q.Parameters.AddWithValue("$state",state.Trim());q.Parameters.AddWithValue("$progress",Math.Clamp(progressPercent,0,100));q.Parameters.AddWithValue("$detail",Db(detail));q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));q.Parameters.AddWithValue("$completed",completed?DemoSqliteDatabase.ToDb(now):DBNull.Value);await q.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TextExtractionStatusSnapshot>> GetExtractionStatusAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT ExtractionKind,State,ProgressPercent,Detail,UpdatedAtUtc,CompletedAtUtc FROM DemoExtractionStatus WHERE AssetId=$asset ORDER BY ExtractionKind;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<TextExtractionStatusSnapshot>();while(await r.ReadAsync(cancellationToken))rows.Add(new TextExtractionStatusSnapshot(assetId,r.GetString(0),r.GetString(1),r.GetInt32(2),r.IsDBNull(3)?null:r.GetString(3),DemoSqliteDatabase.FromDb(r.GetString(4)),r.IsDBNull(5)?null:DemoSqliteDatabase.FromDb(r.GetString(5))));return rows;
    }

    public async Task<DiscoverySearchResult> SearchAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        var normalized=DiscoveryText.Normalize(request.Query);var page=Math.Max(1,request.Page);var pageSize=Math.Clamp(request.PageSize,1,200);if(normalized.Length==0)return new DiscoverySearchResult([],0,page,pageSize,normalized);
        var categories=(await ListCategoriesAsync(cancellationToken)).ToDictionary(x=>x.CategoryId);var rows=new List<DiscoverySearchHit>();await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="""
            SELECT a.AssetId,a.Title,a.MediaKind,a.UpdatedAtUtc,COALESCE(ac.CategoryId,$uncat),t.SourceKind,t.TextValue,t.SegmentsJson
            FROM DemoAsset a LEFT JOIN DemoAssetCategory ac ON ac.AssetId=a.AssetId LEFT JOIN DemoAssetText t ON t.AssetId=a.AssetId
            WHERE NOT EXISTS(SELECT 1 FROM DemoTapeAttachment ta WHERE ta.AssetId=a.AssetId)
            ORDER BY a.UpdatedAtUtc DESC;
            """;q.Parameters.AddWithValue("$uncat",UncategorizedId.ToString("D"));await using var r=await q.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken)){var id=Guid.Parse(r.GetString(0));var title=r.GetString(1);var kind=r.IsDBNull(2)?MediaKinds.Other:r.GetString(2);var catId=Guid.Parse(r.GetString(4));if(request.CategoryId is Guid filterCat&&catId!=filterCat)continue;if(!string.IsNullOrWhiteSpace(request.MediaKind)&&!string.Equals(kind,request.MediaKind.Trim(),StringComparison.OrdinalIgnoreCase))continue;var source=r.IsDBNull(5)?"metadata":r.GetString(5);var text=r.IsDBNull(6)?title:r.GetString(6);var hay=DiscoveryText.Normalize(title+" "+text);if(!hay.Contains(normalized,StringComparison.Ordinal))continue;var category=categories.TryGetValue(catId,out var cv)?cv:categories[UncategorizedId];var segment=BestSegment(r.IsDBNull(7)?"[]":r.GetString(7),normalized);var tags=await ReferenceNamesAsync(id,cancellationToken);rows.Add(new DiscoverySearchHit(id,title,kind,category.CategoryId,category.NameEn,category.NameAr,source,Snippet(segment?.Text??text,request.Query),segment?.StartMs,segment?.EndMs,segment?.PageNumber,tags,DemoSqliteDatabase.FromDb(r.GetString(3))));}
        var distinct=rows.GroupBy(x=>new{x.AssetId,x.MatchedSource,x.StartMs,x.PageNumber}).Select(g=>g.First()).ToArray();return new DiscoverySearchResult(distinct.Skip((page-1)*pageSize).Take(pageSize).ToArray(),distinct.LongLength,page,pageSize,normalized);
    }

    public async Task<IReadOnlyList<ReferenceSubjectSnapshot>> ListReferenceSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var list=new List<ReferenceSubjectSnapshot>();await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT SubjectId,NameEn,NameAr,DescriptionEn,DescriptionAr,TagsJson,IsActive,CreatedAtUtc,UpdatedAtUtc FROM DemoReferenceSubject ORDER BY NameEn;";await using var r=await q.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken)){var id=Guid.Parse(r.GetString(0));list.Add(new ReferenceSubjectSnapshot(id,r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),JsonSerializer.Deserialize<string[]>(r.GetString(5))??[],r.GetInt32(6)!=0,await ReferenceAssetIdsAsync(id,cancellationToken),DemoSqliteDatabase.FromDb(r.GetString(7)),DemoSqliteDatabase.FromDb(r.GetString(8))));}return list;
    }

    public async Task<ReferenceSubjectSnapshot> CreateReferenceSubjectAsync(CreateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateName(request.NameEn);var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoReferenceSubject(SubjectId,NameEn,NameAr,DescriptionEn,DescriptionAr,TagsJson,IsActive,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$en,$ar,$den,$dar,$tags,1,$now,$now);";q.Parameters.AddWithValue("$id",id.ToString("D"));q.Parameters.AddWithValue("$en",request.NameEn.Trim());q.Parameters.AddWithValue("$ar",Db(request.NameAr));q.Parameters.AddWithValue("$den",Db(request.DescriptionEn));q.Parameters.AddWithValue("$dar",Db(request.DescriptionAr));q.Parameters.AddWithValue("$tags",JsonSerializer.Serialize(request.Tags??[]));q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"discovery.reference.created","ReferenceSubject",id.ToString("D"),cancellationToken);return (await ListReferenceSubjectsAsync(cancellationToken)).First(x=>x.SubjectId==id);
    }

    public async Task<ReferenceSubjectSnapshot> UpdateReferenceSubjectAsync(Guid subjectId, UpdateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateName(request.NameEn);
        var now=DateTimeOffset.UtcNow;
        await using var c=await database.OpenAsync(cancellationToken);
        await using var q=c.CreateCommand();
        q.CommandText="UPDATE DemoReferenceSubject SET NameEn=$en,NameAr=$ar,DescriptionEn=$den,DescriptionAr=$dar,TagsJson=$tags,IsActive=$active,UpdatedAtUtc=$now WHERE SubjectId=$id;";
        q.Parameters.AddWithValue("$id",subjectId.ToString("D"));q.Parameters.AddWithValue("$en",request.NameEn.Trim());q.Parameters.AddWithValue("$ar",Db(request.NameAr));q.Parameters.AddWithValue("$den",Db(request.DescriptionEn));q.Parameters.AddWithValue("$dar",Db(request.DescriptionAr));q.Parameters.AddWithValue("$tags",JsonSerializer.Serialize(request.Tags??[]));q.Parameters.AddWithValue("$active",request.IsActive?1:0);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));
        if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new DiscoveryRequestException("subject_not_found","Reference subject was not found.",404);
        await AuditAsync(actorId,"discovery.reference.updated","ReferenceSubject",subjectId.ToString("D"),cancellationToken);
        return (await ListReferenceSubjectsAsync(cancellationToken)).First(x=>x.SubjectId==subjectId);
    }

    public async Task DeleteReferenceSubjectAsync(Guid subjectId, string actorId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);
        await using var tx=await c.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach(var sql in new[]{"DELETE FROM DemoAssetReferenceTag WHERE SubjectId=$id;","DELETE FROM DemoReferenceAsset WHERE SubjectId=$id;"})
            {
                await using var d=c.CreateCommand();d.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;d.CommandText=sql;d.Parameters.AddWithValue("$id",subjectId.ToString("D"));await d.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var q=c.CreateCommand();q.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;q.CommandText="DELETE FROM DemoReferenceSubject WHERE SubjectId=$id;";q.Parameters.AddWithValue("$id",subjectId.ToString("D"));
            if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new DiscoveryRequestException("subject_not_found","Reference subject was not found.",404);
            await tx.CommitAsync(cancellationToken);
        }
        catch { try{await tx.RollbackAsync(CancellationToken.None);}catch{} throw; }
        await AuditAsync(actorId,"discovery.reference.deleted","ReferenceSubject",subjectId.ToString("D"),cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceSubjectUsageSnapshot>> ListReferenceSubjectUsageAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();
        q.CommandText="SELECT SubjectId,AssetId FROM DemoAssetReferenceTag ORDER BY SubjectId,AssetId;";
        await using var r=await q.ExecuteReaderAsync(cancellationToken);var bySubject=new Dictionary<Guid,List<Guid>>();
        while(await r.ReadAsync(cancellationToken)){var subject=Guid.Parse(r.GetString(0));if(!bySubject.TryGetValue(subject,out var ids)){ids=[];bySubject[subject]=ids;}ids.Add(Guid.Parse(r.GetString(1)));}
        return bySubject.Select(x=>new ReferenceSubjectUsageSnapshot(x.Key,x.Value)).ToArray();
    }

    public async Task<ReferenceSubjectSnapshot> AddReferenceImageAsync(Guid subjectId, Guid assetId, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken);if(!(await ListReferenceSubjectsAsync(cancellationToken)).Any(x=>x.SubjectId==subjectId))throw new DiscoveryRequestException("subject_not_found","Reference subject was not found.",404);await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT OR IGNORE INTO DemoReferenceAsset(SubjectId,AssetId) VALUES($subject,$asset);";q.Parameters.AddWithValue("$subject",subjectId.ToString("D"));q.Parameters.AddWithValue("$asset",assetId.ToString("D"));await q.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"discovery.reference.asset-added","ReferenceSubject",subjectId.ToString("D"),cancellationToken);return (await ListReferenceSubjectsAsync(cancellationToken)).First(x=>x.SubjectId==subjectId);
    }

    public async Task<AssetReferenceTagSnapshot> AddAssetReferenceTagAsync(Guid assetId, AddAssetReferenceTagRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        await EnsureAssetAsync(assetId,cancellationToken);var subject=(await ListReferenceSubjectsAsync(cancellationToken)).FirstOrDefault(x=>x.SubjectId==request.SubjectId)??throw new DiscoveryRequestException("subject_not_found","Reference subject was not found.",404);var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoAssetReferenceTag(AssetId,SubjectId,Confidence,DetectionSource,CreatedAtUtc) VALUES($asset,$subject,$confidence,$source,$now) ON CONFLICT(AssetId,SubjectId) DO UPDATE SET Confidence=excluded.Confidence,DetectionSource=excluded.DetectionSource,CreatedAtUtc=excluded.CreatedAtUtc;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));q.Parameters.AddWithValue("$subject",request.SubjectId.ToString("D"));q.Parameters.AddWithValue("$confidence",request.Confidence is decimal conf?(double)conf:DBNull.Value);q.Parameters.AddWithValue("$source",request.DetectionSource.Trim());q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"discovery.asset.reference-tagged","MediaAsset",assetId.ToString("D"),cancellationToken);return new AssetReferenceTagSnapshot(assetId,request.SubjectId,subject.NameEn,subject.NameAr,request.Confidence,request.DetectionSource.Trim(),now);
    }

    public async Task<IReadOnlyList<AssetReferenceTagSnapshot>> ListAssetReferenceTagsAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT t.SubjectId,s.NameEn,s.NameAr,t.Confidence,t.DetectionSource,t.CreatedAtUtc FROM DemoAssetReferenceTag t JOIN DemoReferenceSubject s ON s.SubjectId=t.SubjectId WHERE t.AssetId=$asset ORDER BY s.NameEn;";q.Parameters.AddWithValue("$asset",assetId.ToString("D"));await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<AssetReferenceTagSnapshot>();while(await r.ReadAsync(cancellationToken))rows.Add(new AssetReferenceTagSnapshot(assetId,Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:(decimal)r.GetDouble(3),r.GetString(4),DemoSqliteDatabase.FromDb(r.GetString(5))));return rows;
    }

    public async Task<IReadOnlyList<MediaPermissionSnapshot>> ListMediaPermissionsAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload,UpdatedAtUtc FROM DemoMediaPermission ORDER BY RoleName,MediaKind;";await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<MediaPermissionSnapshot>();while(await r.ReadAsync(cancellationToken))rows.Add(new MediaPermissionSnapshot(r.GetString(0),r.GetString(1),r.GetInt32(2)!=0,r.GetInt32(3)!=0,r.GetInt32(4)!=0,r.GetInt32(5)!=0,r.GetInt32(6)!=0,DemoSqliteDatabase.FromDb(r.GetString(7))));return rows;
    }

    public async Task<MediaPermissionSnapshot> UpsertMediaPermissionAsync(UpsertMediaPermissionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(request.RoleName)||string.IsNullOrWhiteSpace(request.MediaKind))throw new DiscoveryRequestException("permission_key_required","Role and media kind are required.");var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoMediaPermission(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload,UpdatedAtUtc) VALUES($role,$kind,$view,$upload,$edit,$process,$download,$now) ON CONFLICT(RoleName,MediaKind) DO UPDATE SET CanView=excluded.CanView,CanUpload=excluded.CanUpload,CanEdit=excluded.CanEdit,CanProcess=excluded.CanProcess,CanDownload=excluded.CanDownload,UpdatedAtUtc=excluded.UpdatedAtUtc;";q.Parameters.AddWithValue("$role",request.RoleName.Trim());q.Parameters.AddWithValue("$kind",request.MediaKind.Trim());q.Parameters.AddWithValue("$view",request.CanView?1:0);q.Parameters.AddWithValue("$upload",request.CanUpload?1:0);q.Parameters.AddWithValue("$edit",request.CanEdit?1:0);q.Parameters.AddWithValue("$process",request.CanProcess?1:0);q.Parameters.AddWithValue("$download",request.CanDownload?1:0);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await AuditAsync(actorId,"discovery.permission.updated","MediaPermission",request.RoleName+":"+request.MediaKind,cancellationToken);return new MediaPermissionSnapshot(request.RoleName.Trim(),request.MediaKind.Trim(),request.CanView,request.CanUpload,request.CanEdit,request.CanProcess,request.CanDownload,now);
    }

    public async Task<bool> IsMediaActionAllowedAsync(IEnumerable<string> roles, string mediaKind, string action, CancellationToken cancellationToken = default)
    {
        var roleSet=roles.Where(x=>!string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);if(roleSet.Contains("Administrator"))return true;var permissions=await ListMediaPermissionsAsync(cancellationToken);foreach(var p in permissions.Where(p=>roleSet.Contains(p.RoleName)&&string.Equals(p.MediaKind,mediaKind,StringComparison.OrdinalIgnoreCase))){if(action.ToLowerInvariant() switch{"view"=>p.CanView,"upload"=>p.CanUpload,"edit"=>p.CanEdit,"process"=>p.CanProcess,"download"=>p.CanDownload,_=>false})return true;}return false;
    }

    public async Task<string> GetAssetMediaKindAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT MediaKind,OriginalFileName FROM DemoAsset WHERE AssetId=$id;";q.Parameters.AddWithValue("$id",assetId.ToString("D"));await using var r=await q.ExecuteReaderAsync(cancellationToken);if(!await r.ReadAsync(cancellationToken))throw new DiscoveryRequestException("asset_not_found","Asset was not found.",404);return r.IsDBNull(0)?MediaKinds.FromFileName(r.IsDBNull(1)?null:r.GetString(1)):r.GetString(0);
    }

    public async Task<DiscoveryDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);async Task<long> Count(string sql){await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(await q.ExecuteScalarAsync(cancellationToken));}return new DiscoveryDashboardSnapshot(await Count("SELECT COUNT(*) FROM DemoCategory;"),await Count($"SELECT COUNT(*) FROM DemoAsset a WHERE NOT EXISTS(SELECT 1 FROM DemoAssetCategory ac WHERE ac.AssetId=a.AssetId) OR EXISTS(SELECT 1 FROM DemoAssetCategory ac WHERE ac.AssetId=a.AssetId AND ac.CategoryId='{UncategorizedId:D}');"),await Count("SELECT COUNT(DISTINCT AssetId) FROM DemoAssetText;"),await Count("SELECT COUNT(*) FROM DemoAssetText WHERE SourceKind='transcript';"),await Count("SELECT COUNT(*) FROM DemoAssetText WHERE SourceKind='ocr';"),await Count("SELECT COUNT(*) FROM DemoReferenceSubject WHERE IsActive=1;"));
    }

    private async Task SeedPermissionsAsync(CancellationToken ct){await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();var now=DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow);q.CommandText="""
        INSERT OR IGNORE INTO DemoMediaPermission(RoleName,MediaKind,CanView,CanUpload,CanEdit,CanProcess,CanDownload,UpdatedAtUtc)
        SELECT role,kind,1,CASE WHEN role='Viewer' THEN 0 ELSE 1 END,CASE WHEN role='Viewer' THEN 0 ELSE 1 END,CASE WHEN role='Viewer' THEN 0 ELSE 1 END,1,$now
        FROM (SELECT 'Administrator' role UNION ALL SELECT 'CatalogEditor' UNION ALL SELECT 'Viewer') CROSS JOIN (SELECT 'Video' kind UNION ALL SELECT 'Audio' UNION ALL SELECT 'Image' UNION ALL SELECT 'Document' UNION ALL SELECT 'Other');
        """;q.Parameters.AddWithValue("$now",now);await q.ExecuteNonQueryAsync(ct);}
    private async Task EnsureAssetAsync(Guid id,CancellationToken ct){await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM DemoAsset WHERE AssetId=$id;";q.Parameters.AddWithValue("$id",id.ToString("D"));if(Convert.ToInt64(await q.ExecuteScalarAsync(ct))==0)throw new DiscoveryRequestException("asset_not_found","Asset was not found.",404);}
    private async Task EnsureCategoryAsync(Guid id,CancellationToken ct){if(!(await ListCategoriesAsync(ct)).Any(x=>x.CategoryId==id))throw new DiscoveryRequestException("category_not_found","Category was not found.",404);}
    private async Task<Guid?> AssetCategoryIdAsync(Guid id,CancellationToken ct){await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT CategoryId FROM DemoAssetCategory WHERE AssetId=$id;";q.Parameters.AddWithValue("$id",id.ToString("D"));var v=await q.ExecuteScalarAsync(ct);return v is string s?Guid.Parse(s):null;}
    private async Task<bool> IsDescendantAsync(Guid candidateParent,Guid category,CancellationToken ct){var current=(Guid?)candidateParent;for(var i=0;i<64&&current is Guid id;i++){if(id==category)return true;await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT ParentCategoryId FROM DemoCategory WHERE CategoryId=$id;";q.Parameters.AddWithValue("$id",id.ToString("D"));var v=await q.ExecuteScalarAsync(ct);current=v is string s&&Guid.TryParse(s,out var next)?next:null;}return false;}
    private async Task<IReadOnlyList<Guid>> ReferenceAssetIdsAsync(Guid subject,CancellationToken ct){await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT AssetId FROM DemoReferenceAsset WHERE SubjectId=$id;";q.Parameters.AddWithValue("$id",subject.ToString("D"));await using var r=await q.ExecuteReaderAsync(ct);var ids=new List<Guid>();while(await r.ReadAsync(ct))ids.Add(Guid.Parse(r.GetString(0)));return ids;}
    private async Task<IReadOnlyList<string>> ReferenceNamesAsync(Guid asset,CancellationToken ct){return (await ListAssetReferenceTagsAsync(asset,ct)).Select(x=>x.NameEn).ToArray();}
    private async Task AuditAsync(string actor,string action,string type,string id,CancellationToken ct)=>await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,action,type,id,"Success","provider=SqliteDemo"),ct);
    private static CategorySnapshot ReadCategory(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),r.IsDBNull(1)?null:Guid.Parse(r.GetString(1)),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetInt32(4)!=0,r.GetInt32(5),r.GetInt64(6),r.GetInt32(7),r.GetInt32(8),DemoSqliteDatabase.FromDb(r.GetString(9)),DemoSqliteDatabase.FromDb(r.GetString(10)));
    private static IReadOnlyList<TextSegmentSnapshot> Segments(string json){try{return JsonSerializer.Deserialize<TextSegmentSnapshot[]>(json)??[];}catch{return [];}}
    private static TextSegmentSnapshot? BestSegment(string json,string normalized){return Segments(json).FirstOrDefault(x=>DiscoveryText.Normalize(x.Text).Contains(normalized,StringComparison.Ordinal));}
    private static string Snippet(string text,string? query){var v=(text??"").Replace("\r"," ").Replace("\n"," ").Trim();if(v.Length<=280)return v;var q=query?.Trim()??"";var i=q.Length==0?0:v.IndexOf(q,StringComparison.OrdinalIgnoreCase);var start=Math.Max(0,i<0?0:i-100);return v.Substring(start,Math.Min(280,v.Length-start));}
    private static object Db(string? v)=>string.IsNullOrWhiteSpace(v)?DBNull.Value:v.Trim();
    private static void ValidateName(string? v){var n=v?.Trim()??"";if(n.Length is 0 or >200)throw new DiscoveryRequestException("invalid_name","Name is required and must not exceed 200 characters.");}
}
