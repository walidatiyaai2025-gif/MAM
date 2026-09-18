using System.Text.Json;
using MAM.Application.Auditing;
using MAM.Application.Curation;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Demo;

public sealed class DemoCurationService(DemoSqliteDatabase database, IAuditSink audit) : ICurationService
{
    public CurationPolicy Policy { get; } = new(true, "Offline demo filters are stored in the embedded catalog.", 200, 100);

    public async ValueTask<CurationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await database.EnsureInitializedAsync(cancellationToken);
        return new CurationHealth(true, "SqliteDemo", "Offline demo search and metadata curation are ready.");
    }

    public async ValueTask<CurationSearchResult> SearchAsync(CurationSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page); var pageSize = Math.Clamp(request.PageSize, 1, Policy.MaxPageSize);
        var all = await ReadAssetsAsync(cancellationToken);
        IEnumerable<CurationAssetItem> query = all;
        var normalized = CurationTextNormalizer.NormalizeSearch(request.Query);
        if (normalized.Length > 0) query = query.Where(a => CurationTextNormalizer.NormalizeSearch(string.Join(' ', new[] { a.Title, a.TitleAr, a.Category, string.Join(' ', a.Tags) })).Contains(normalized, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(request.Lifecycle)) query = query.Where(a => string.Equals(a.Lifecycle, request.Lifecycle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.Category)) query = query.Where(a => string.Equals(a.Category, request.Category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.Tag)) query = query.Where(a => a.Tags.Contains(request.Tag.Trim(), StringComparer.OrdinalIgnoreCase));
        if (request.CollectionId is Guid collectionId)
        {
            var ids = await CollectionAssetIdsAsync(collectionId, cancellationToken);
            query = query.Where(a => ids.Contains(a.Id));
        }
        var materialized = query.OrderByDescending(a => a.UpdatedAtUtc).ToArray();
        var facets = new CurationFacets(
            materialized.GroupBy(a => a.Lifecycle,StringComparer.OrdinalIgnoreCase).Select(g=>new CurationFacetValue(g.Key,g.LongCount())).OrderByDescending(x=>x.Count).ToArray(),
            materialized.Where(a=>!string.IsNullOrWhiteSpace(a.Category)).GroupBy(a=>a.Category!,StringComparer.OrdinalIgnoreCase).Select(g=>new CurationFacetValue(g.Key,g.LongCount())).OrderByDescending(x=>x.Count).ToArray(),
            materialized.SelectMany(a=>a.Tags).GroupBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(g=>new CurationFacetValue(g.Key,g.LongCount())).OrderByDescending(x=>x.Count).ToArray());
        return new CurationSearchResult(materialized.Skip((page-1)*pageSize).Take(pageSize).ToArray(), materialized.LongLength, page, pageSize, facets);
    }

    public async ValueTask<AssetMetadataSnapshot?> GetMetadataAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand();
        q.CommandText="SELECT Title,TitleAr,EventDate,Category,TagsJson,PreservationNotes,Lifecycle,Version,UpdatedAtUtc FROM DemoAsset WHERE AssetId=$id;"; q.Parameters.AddWithValue("$id",assetId.ToString("D")); await using var r=await q.ExecuteReaderAsync(cancellationToken); if(!await r.ReadAsync(cancellationToken)) return null;
        return Metadata(assetId,r);
    }

    public async ValueTask<AssetMetadataSnapshot> UpdateMetadataAsync(Guid assetId, AssetMetadataUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateMetadata(request.SchemaKey,request.TitleEn,request.TitleAr,request.Tags);
        var now=DateTimeOffset.UtcNow; var tags=CurationTextNormalizer.NormalizeTags(request.Tags);
        foreach(var tag in tags)await EnsureDemoTagAsync(tag,cancellationToken);
        var dictionary=(await ListTagsAsync(null,cancellationToken)).ToDictionary(x=>x.NormalizedName,x=>x.Name,StringComparer.Ordinal);
        tags=tags.Select(x=>dictionary.TryGetValue(CurationTextNormalizer.NormalizeSearch(x),out var canonical)?canonical:x).ToArray();
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand();
        q.CommandText="""
            UPDATE DemoAsset SET Title=$title,TitleAr=$titleAr,EventDate=$eventDate,Category=$category,TagsJson=$tags,PreservationNotes=$notes,Version=Version+1,UpdatedAtUtc=$now
            WHERE AssetId=$id AND Version=$version;
            """;
        q.Parameters.AddWithValue("$title",request.TitleEn.Trim()); q.Parameters.AddWithValue("$titleAr",Db(request.TitleAr)); q.Parameters.AddWithValue("$eventDate",Db(request.EventDate?.ToString("yyyy-MM-dd"))); q.Parameters.AddWithValue("$category",Db(request.Category)); q.Parameters.AddWithValue("$tags",JsonSerializer.Serialize(tags)); q.Parameters.AddWithValue("$notes",Db(request.PreservationNotes)); q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now)); q.Parameters.AddWithValue("$id",assetId.ToString("D")); q.Parameters.AddWithValue("$version",request.ExpectedVersion);
        if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)
        {
            var current=await GetMetadataAsync(assetId,cancellationToken); if(current is null) throw new CurationRequestException("asset_not_found","Asset was not found.",404); throw new CurationRequestException("version_conflict","The asset changed. Refresh and retry.",409,current);
        }
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actorId,"curation.metadata.updated","MediaAsset",assetId.ToString("D"),"Success","provider=SqliteDemo"),cancellationToken);
        return (await GetMetadataAsync(assetId,cancellationToken))!;
    }

    public async ValueTask<BulkMetadataResult> BulkUpdateMetadataAsync(BulkMetadataRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if(request.Items.Count>Policy.MaxBulkItems) throw new CurationRequestException("bulk_limit_exceeded",$"Bulk update is limited to {Policy.MaxBulkItems} items.",400);
        var results=new List<BulkMetadataItemResult>();
        foreach(var item in request.Items)
        {
            try
            {
                var current=await UpdateMetadataAsync(item.AssetId,new AssetMetadataUpdateRequest(item.ExpectedVersion,item.SchemaKey,item.TitleEn,item.TitleAr,item.EventDate,item.Category,item.Tags,item.PreservationNotes),actorId,cancellationToken);
                results.Add(new BulkMetadataItemResult(item.AssetId,true,"Updated",null,current));
            }
            catch(CurationRequestException ex){ results.Add(new BulkMetadataItemResult(item.AssetId,false,ex.Code,ex.Message,ex.Current as AssetMetadataSnapshot)); }
        }
        return new BulkMetadataResult(results.Count,results.Count(x=>x.Succeeded),results.Count(x=>!x.Succeeded),results);
    }

    public async ValueTask<IReadOnlyList<CollectionSnapshot>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand(); q.CommandText="""
            SELECT c.CollectionId,c.NameEn,c.NameAr,c.Version,COUNT(ca.AssetId),c.CreatedAtUtc,c.UpdatedAtUtc FROM DemoCollection c LEFT JOIN DemoCollectionAsset ca ON ca.CollectionId=c.CollectionId GROUP BY c.CollectionId ORDER BY c.UpdatedAtUtc DESC;
            """; await using var r=await q.ExecuteReaderAsync(cancellationToken); var rows=new List<CollectionSnapshot>(); while(await r.ReadAsync(cancellationToken)) rows.Add(Collection(r)); return rows;
    }

    public async ValueTask<CollectionSnapshot> CreateCollectionAsync(CreateCollectionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var name=request.NameEn?.Trim()??""; if(name.Length is 0 or >200) throw new CurationRequestException("invalid_collection_name","Collection name is required and must not exceed 200 characters.",400);
        var id=Guid.NewGuid(); var now=DateTimeOffset.UtcNow; await using var c=await database.OpenAsync(cancellationToken); await EnsureCollectionNameAvailableAsync(c,null,name,null,cancellationToken); await using var q=c.CreateCommand(); q.CommandText="INSERT INTO DemoCollection(CollectionId,NameEn,NameAr,Version,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$en,$ar,1,$now,$now);"; q.Parameters.AddWithValue("$id",id.ToString("D")); q.Parameters.AddWithValue("$en",name); q.Parameters.AddWithValue("$ar",Db(request.NameAr)); q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now)); await q.ExecuteNonQueryAsync(cancellationToken); await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actorId,"curation.collection.created","Collection",id.ToString("D"),"Success"),cancellationToken); return (await ListCollectionsAsync(cancellationToken)).First(x=>x.CollectionId==id);
    }

    public async ValueTask<CollectionSnapshot> UpdateCollectionAsync(Guid collectionId,UpdateCollectionRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        if(request is null||request.ExpectedVersion<1)throw new CurationRequestException("invalid_collection_version","A valid expected collection version is required.",400);
        var name=request.NameEn?.Trim()??"";if(name.Length is 0 or >200)throw new CurationRequestException("invalid_collection_name","Collection name is required and must not exceed 200 characters.",400);
        var current=(await ListCollectionsAsync(cancellationToken)).FirstOrDefault(x=>x.CollectionId==collectionId)??throw new CurationRequestException("collection_not_found","Collection was not found.",404);
        if(current.Version!=request.ExpectedVersion)throw new CurationRequestException("concurrency_conflict","Collection changed since it was loaded.",409,current);
        await using var c=await database.OpenAsync(cancellationToken);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);await EnsureCollectionNameAvailableAsync(c,tx,name,collectionId,cancellationToken);
        await using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE DemoCollection SET NameEn=$en,NameAr=$ar,Version=Version+1,UpdatedAtUtc=$now WHERE CollectionId=$id AND Version=$version;";q.Parameters.AddWithValue("$en",name);q.Parameters.AddWithValue("$ar",Db(request.NameAr));q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));q.Parameters.AddWithValue("$id",collectionId.ToString("D"));q.Parameters.AddWithValue("$version",request.ExpectedVersion);if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new CurationRequestException("concurrency_conflict","Collection changed while update was being applied.",409,current);await tx.CommitAsync(cancellationToken);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actorId,"curation.collection.updated","Collection",collectionId.ToString("D"),"Success"),cancellationToken);return (await ListCollectionsAsync(cancellationToken)).First(x=>x.CollectionId==collectionId);
    }

    public async ValueTask DeleteCollectionAsync(Guid collectionId,long expectedVersion,string actorId,CancellationToken cancellationToken=default)
    {
        var current=(await ListCollectionsAsync(cancellationToken)).FirstOrDefault(x=>x.CollectionId==collectionId)??throw new CurationRequestException("collection_not_found","Collection was not found.",404);if(current.Version!=expectedVersion)throw new CurationRequestException("concurrency_conflict","Collection changed since it was loaded.",409,current);
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="DELETE FROM DemoCollection WHERE CollectionId=$id AND Version=$version;";q.Parameters.AddWithValue("$id",collectionId.ToString("D"));q.Parameters.AddWithValue("$version",expectedVersion);if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new CurationRequestException("concurrency_conflict","Collection changed while deletion was being applied.",409,current);
        await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actorId,"curation.collection.deleted","Collection",collectionId.ToString("D"),"Success",$"membersDetached={current.MemberCount};assetsDeleted=0"),cancellationToken);
    }

    public ValueTask<CollectionSnapshot> AddToCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default)=>ChangeMembershipAsync(collectionId,assetId,expectedVersion,true,actorId,cancellationToken);
    public ValueTask<CollectionSnapshot> RemoveFromCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default)=>ChangeMembershipAsync(collectionId,assetId,expectedVersion,false,actorId,cancellationToken);

    public async ValueTask<IReadOnlyList<TagSnapshot>> ListTagsAsync(string? query=null,CancellationToken cancellationToken=default)
    {
        await SyncTagsFromAssetsAsync(cancellationToken);var normalized=CurationTextNormalizer.NormalizeSearch(query);var assets=await ReadAssetsAsync(cancellationToken);
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT TagId,Name,NormalizedName,Version,CreatedAtUtc,UpdatedAtUtc FROM DemoTag ORDER BY Name;";await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<TagSnapshot>();
        while(await r.ReadAsync(cancellationToken)){var n=r.GetString(2);if(normalized.Length>0&&!n.Contains(normalized,StringComparison.Ordinal))continue;var count=assets.Count(a=>a.Tags.Any(t=>CurationTextNormalizer.NormalizeSearch(t)==n));rows.Add(new TagSnapshot(Guid.Parse(r.GetString(0)),r.GetString(1),n,r.GetInt64(3),count,DemoSqliteDatabase.FromDb(r.GetString(4)),DemoSqliteDatabase.FromDb(r.GetString(5))));}
        return rows.OrderByDescending(x=>x.AssetCount).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async ValueTask<TagSnapshot> CreateTagAsync(CreateTagRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        var name=ValidateTagName(request?.Name);var normalized=CurationTextNormalizer.NormalizeSearch(name);await SyncTagsFromAssetsAsync(cancellationToken);var existing=(await ListTagsAsync(null,cancellationToken)).FirstOrDefault(x=>x.NormalizedName==normalized);if(existing is not null)throw new CurationRequestException("tag_duplicate","A tag with the same normalized name already exists.",409,existing);
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoTag(TagId,Name,NormalizedName,Version,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$name,$normalized,1,$now,$now);";q.Parameters.AddWithValue("$id",id.ToString("D"));q.Parameters.AddWithValue("$name",name);q.Parameters.AddWithValue("$normalized",normalized);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actorId,"curation.tag.created","Tag",id.ToString("D"),"Success",$"name={name}"),cancellationToken);return new TagSnapshot(id,name,normalized,1,0,now,now);
    }

    public async ValueTask<TagSnapshot> UpdateTagAsync(Guid tagId,UpdateTagRequest request,string actorId,CancellationToken cancellationToken=default)
    {
        var tags=await ListTagsAsync(null,cancellationToken);var current=tags.FirstOrDefault(x=>x.TagId==tagId)??throw new CurationRequestException("tag_not_found","Tag was not found.",404);if(current.Version!=request.ExpectedVersion)throw new CurationRequestException("concurrency_conflict","Tag changed since it was loaded.",409,current);
        var name=ValidateTagName(request.Name);var normalized=CurationTextNormalizer.NormalizeSearch(name);var duplicate=tags.FirstOrDefault(x=>x.TagId!=tagId&&x.NormalizedName==normalized);if(duplicate is not null)throw new CurationRequestException("tag_duplicate","A tag with the same normalized name already exists.",409,duplicate);
        await using var c=await database.OpenAsync(cancellationToken);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        var changes=new List<(Guid Id,string Json)>();await using(var read=c.CreateCommand()){read.Transaction=tx;read.CommandText="SELECT AssetId,TagsJson FROM DemoAsset;";await using var rr=await read.ExecuteReaderAsync(cancellationToken);while(await rr.ReadAsync(cancellationToken)){var id=Guid.Parse(rr.GetString(0));var values=Tags(rr.GetString(1));if(!values.Any(x=>CurationTextNormalizer.NormalizeSearch(x)==current.NormalizedName))continue;var renamed=values.Select(x=>CurationTextNormalizer.NormalizeSearch(x)==current.NormalizedName?name:x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();changes.Add((id,JsonSerializer.Serialize(renamed)));}}
        foreach(var change in changes){await using var up=c.CreateCommand();up.Transaction=tx;up.CommandText="UPDATE DemoAsset SET TagsJson=$tags,Version=Version+1,UpdatedAtUtc=$now WHERE AssetId=$id;";up.Parameters.AddWithValue("$tags",change.Json);up.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));up.Parameters.AddWithValue("$id",change.Id.ToString("D"));await up.ExecuteNonQueryAsync(cancellationToken);}
        await using(var q=c.CreateCommand()){q.Transaction=tx;q.CommandText="UPDATE DemoTag SET Name=$name,NormalizedName=$normalized,Version=Version+1,UpdatedAtUtc=$now WHERE TagId=$id AND Version=$version;";q.Parameters.AddWithValue("$name",name);q.Parameters.AddWithValue("$normalized",normalized);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));q.Parameters.AddWithValue("$id",tagId.ToString("D"));q.Parameters.AddWithValue("$version",request.ExpectedVersion);if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new CurationRequestException("concurrency_conflict","Tag changed while update was being applied.",409,current);}
        await tx.CommitAsync(cancellationToken);await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actorId,"curation.tag.updated","Tag",tagId.ToString("D"),"Success",$"assets={changes.Count}"),cancellationToken);return (await ListTagsAsync(null,cancellationToken)).First(x=>x.TagId==tagId);
    }

    public async ValueTask<TagDeletionResult> DeleteTagAsync(Guid tagId,long expectedVersion,bool removeFromAssets,string actorId,CancellationToken cancellationToken=default)
    {
        var current=(await ListTagsAsync(null,cancellationToken)).FirstOrDefault(x=>x.TagId==tagId)??throw new CurationRequestException("tag_not_found","Tag was not found.",404);if(current.Version!=expectedVersion)throw new CurationRequestException("concurrency_conflict","Tag changed since it was loaded.",409,current);if(current.AssetCount>0&&!removeFromAssets)throw new CurationRequestException("tag_in_use","This tag is assigned to assets. Confirm removal from all assets before deleting it.",409,current);
        var changes=new List<(Guid Id,string Json)>();await using var c=await database.OpenAsync(cancellationToken);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        if(removeFromAssets){await using(var read=c.CreateCommand()){read.Transaction=tx;read.CommandText="SELECT AssetId,TagsJson FROM DemoAsset;";await using var rr=await read.ExecuteReaderAsync(cancellationToken);while(await rr.ReadAsync(cancellationToken)){var id=Guid.Parse(rr.GetString(0));var values=Tags(rr.GetString(1));var filtered=values.Where(x=>CurationTextNormalizer.NormalizeSearch(x)!=current.NormalizedName).ToArray();if(filtered.Length!=values.Count)changes.Add((id,JsonSerializer.Serialize(filtered)));}}foreach(var change in changes){await using var up=c.CreateCommand();up.Transaction=tx;up.CommandText="UPDATE DemoAsset SET TagsJson=$tags,Version=Version+1,UpdatedAtUtc=$now WHERE AssetId=$id;";up.Parameters.AddWithValue("$tags",change.Json);up.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));up.Parameters.AddWithValue("$id",change.Id.ToString("D"));await up.ExecuteNonQueryAsync(cancellationToken);}}
        await using(var q=c.CreateCommand()){q.Transaction=tx;q.CommandText="DELETE FROM DemoTag WHERE TagId=$id AND Version=$version;";q.Parameters.AddWithValue("$id",tagId.ToString("D"));q.Parameters.AddWithValue("$version",expectedVersion);if(await q.ExecuteNonQueryAsync(cancellationToken)!=1)throw new CurationRequestException("concurrency_conflict","Tag changed while deletion was being applied.",409,current);}await tx.CommitAsync(cancellationToken);await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actorId,"curation.tag.deleted","Tag",tagId.ToString("D"),"Success",$"assetsDetached={changes.Count}"),cancellationToken);return new TagDeletionResult(tagId,current.Name,changes.Count);
    }

    public async ValueTask<AssetMetadataSnapshot> SetArchivedAsync(Guid assetId, bool archived, long expectedVersion, string actorId, CancellationToken cancellationToken = default)
    {
        var now=DateTimeOffset.UtcNow; await using var c=await database.OpenAsync(cancellationToken); await using var q=c.CreateCommand(); q.CommandText="UPDATE DemoAsset SET Lifecycle=$state,Version=Version+1,UpdatedAtUtc=$now WHERE AssetId=$id AND Version=$version;"; q.Parameters.AddWithValue("$state",archived?"Archived":"Draft"); q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now)); q.Parameters.AddWithValue("$id",assetId.ToString("D")); q.Parameters.AddWithValue("$version",expectedVersion); if(await q.ExecuteNonQueryAsync(cancellationToken)!=1){var current=await GetMetadataAsync(assetId,cancellationToken); if(current is null) throw new CurationRequestException("asset_not_found","Asset was not found.",404); throw new CurationRequestException("version_conflict","The asset changed. Refresh and retry.",409,current);} await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actorId,archived?"curation.asset.archived":"curation.asset.restored","MediaAsset",assetId.ToString("D"),"Success"),cancellationToken); return (await GetMetadataAsync(assetId,cancellationToken))!;
    }

    private async ValueTask<CollectionSnapshot> ChangeMembershipAsync(Guid collectionId,Guid assetId,long expectedVersion,bool add,string actorId,CancellationToken ct)
    {
        await using var c=await database.OpenAsync(ct); await using var tx=await c.BeginTransactionAsync(ct);
        long current; await using(var read=c.CreateCommand()){read.Transaction=(SqliteTransaction)tx; read.CommandText="SELECT Version FROM DemoCollection WHERE CollectionId=$id;"; read.Parameters.AddWithValue("$id",collectionId.ToString("D")); var value=await read.ExecuteScalarAsync(ct); if(value is null) throw new CurationRequestException("collection_not_found","Collection was not found.",404); current=Convert.ToInt64(value);}
        if(current!=expectedVersion) throw new CurationRequestException("version_conflict","The collection changed. Refresh and retry.",409);
        await using(var exists=c.CreateCommand()){exists.Transaction=(SqliteTransaction)tx; exists.CommandText="SELECT COUNT(*) FROM DemoAsset WHERE AssetId=$id;"; exists.Parameters.AddWithValue("$id",assetId.ToString("D")); if(Convert.ToInt64(await exists.ExecuteScalarAsync(ct))==0) throw new CurationRequestException("asset_not_found","Asset was not found.",404);}
        await using(var change=c.CreateCommand()){change.Transaction=(SqliteTransaction)tx; change.CommandText=add?"INSERT OR IGNORE INTO DemoCollectionAsset(CollectionId,AssetId) VALUES($collection,$asset);":"DELETE FROM DemoCollectionAsset WHERE CollectionId=$collection AND AssetId=$asset;"; change.Parameters.AddWithValue("$collection",collectionId.ToString("D")); change.Parameters.AddWithValue("$asset",assetId.ToString("D")); await change.ExecuteNonQueryAsync(ct);}
        var now=DateTimeOffset.UtcNow; await using(var bump=c.CreateCommand()){bump.Transaction=(SqliteTransaction)tx; bump.CommandText="UPDATE DemoCollection SET Version=Version+1,UpdatedAtUtc=$now WHERE CollectionId=$id;"; bump.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now)); bump.Parameters.AddWithValue("$id",collectionId.ToString("D")); await bump.ExecuteNonQueryAsync(ct);} await tx.CommitAsync(ct); await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),now,actorId,add?"curation.collection.asset-added":"curation.collection.asset-removed","Collection",collectionId.ToString("D"),"Success",$"asset={assetId:D}"),ct); return (await ListCollectionsAsync(ct)).First(x=>x.CollectionId==collectionId);
    }

    private async Task<IReadOnlyList<CurationAssetItem>> ReadAssetsAsync(CancellationToken ct)
    {
        await using var c=await database.OpenAsync(ct); await using var q=c.CreateCommand(); q.CommandText="""
            SELECT a.AssetId,a.Title,a.TitleAr,a.Lifecycle,a.Version,a.EventDate,a.Category,a.TagsJson,a.PreservationNotes,a.UpdatedAtUtc,COUNT(ca.CollectionId)
            FROM DemoAsset a LEFT JOIN DemoCollectionAsset ca ON ca.AssetId=a.AssetId GROUP BY a.AssetId ORDER BY a.UpdatedAtUtc DESC;
            """; await using var r=await q.ExecuteReaderAsync(ct); var rows=new List<CurationAssetItem>(); while(await r.ReadAsync(ct)){rows.Add(new CurationAssetItem(Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.GetInt64(4),r.IsDBNull(5)?null:DateOnly.Parse(r.GetString(5)),r.IsDBNull(6)?null:r.GetString(6),Tags(r.GetString(7)),r.IsDBNull(8)?null:r.GetString(8),DemoSqliteDatabase.FromDb(r.GetString(9)),r.GetInt32(10)));} return rows;
    }
    private async Task EnsureCollectionNameAvailableAsync(SqliteConnection c,SqliteTransaction? tx,string name,Guid? exclude,CancellationToken ct){await using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT COUNT(*) FROM DemoCollection WHERE lower(trim(NameEn))=lower(trim($name)) AND ($exclude IS NULL OR CollectionId<>$exclude);";q.Parameters.AddWithValue("$name",name);q.Parameters.AddWithValue("$exclude",exclude is Guid id?id.ToString("D"):DBNull.Value);if(Convert.ToInt64(await q.ExecuteScalarAsync(ct))>0)throw new CurationRequestException("collection_duplicate","A collection with the same English name already exists.",409);}
    private async Task SyncTagsFromAssetsAsync(CancellationToken ct){var assets=await ReadAssetsAsync(ct);foreach(var tag in assets.SelectMany(x=>x.Tags).Distinct(StringComparer.OrdinalIgnoreCase))await EnsureDemoTagAsync(tag,ct);}
    private async Task EnsureDemoTagAsync(string raw,CancellationToken ct){var name=ValidateTagName(raw);var normalized=CurationTextNormalizer.NormalizeSearch(name);await using var c=await database.OpenAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT OR IGNORE INTO DemoTag(TagId,Name,NormalizedName,Version,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$name,$normalized,1,$now,$now);";q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("D"));q.Parameters.AddWithValue("$name",name);q.Parameters.AddWithValue("$normalized",normalized);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));await q.ExecuteNonQueryAsync(ct);}
    private static string ValidateTagName(string? value){var name=value?.Trim()??"";if(name.Length is 0 or >120||CurationTextNormalizer.NormalizeSearch(name).Length==0)throw new CurationRequestException("invalid_tag_name","Tag name is required, searchable and must not exceed 120 characters.",400);return name;}
    private async Task<HashSet<Guid>> CollectionAssetIdsAsync(Guid id,CancellationToken ct){await using var c=await database.OpenAsync(ct); await using var q=c.CreateCommand();q.CommandText="SELECT AssetId FROM DemoCollectionAsset WHERE CollectionId=$id;";q.Parameters.AddWithValue("$id",id.ToString("D"));await using var r=await q.ExecuteReaderAsync(ct);var set=new HashSet<Guid>();while(await r.ReadAsync(ct))set.Add(Guid.Parse(r.GetString(0)));return set;}
    private static AssetMetadataSnapshot Metadata(Guid id,SqliteDataReader r)=>new(id,"default",r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:DateOnly.Parse(r.GetString(2)),r.IsDBNull(3)?null:r.GetString(3),Tags(r.GetString(4)),r.IsDBNull(5)?null:r.GetString(5),r.GetString(6),r.GetInt64(7),DemoSqliteDatabase.FromDb(r.GetString(8)));
    private static CollectionSnapshot Collection(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetInt64(3),r.GetInt32(4),DemoSqliteDatabase.FromDb(r.GetString(5)),DemoSqliteDatabase.FromDb(r.GetString(6)));
    private static IReadOnlyList<string> Tags(string json){try{return JsonSerializer.Deserialize<string[]>(json)??[];}catch{return [];}}
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value.Trim();
    private static void ValidateMetadata(string schema,string title,string? titleAr,IReadOnlyList<string>? tags){if(string.IsNullOrWhiteSpace(schema))throw new CurationRequestException("schema_required","Metadata schema is required.",400);if(string.IsNullOrWhiteSpace(title)||title.Trim().Length>300)throw new CurationRequestException("invalid_title","English title is required and must not exceed 300 characters.",400);if(titleAr?.Trim().Length>300)throw new CurationRequestException("invalid_title_ar","Arabic title must not exceed 300 characters.",400);if((tags?.Count??0)>100)throw new CurationRequestException("too_many_tags","No more than 100 tags are allowed.",400);}
}
