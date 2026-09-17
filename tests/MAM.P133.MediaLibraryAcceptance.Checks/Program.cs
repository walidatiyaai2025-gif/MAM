using MAM.Application.MediaLibrary;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Discovery;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task ExecuteAsync(DemoSqliteDatabase database, string sql, params (string Name, object Value)[] parameters)
{
    await using var connection = await database.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    await command.ExecuteNonQueryAsync();
}

var root = Path.Combine(Path.GetTempPath(), "mam-p133-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var dbPath = Path.Combine(root, "mam-demo.db");
var settings = new MamSettings
{
    Environment = new EnvironmentSettings { Name = "Demo" },
    Database = new DatabaseSettings { Provider = "Sqlite", SqlitePath = dbPath, CommandTimeoutSeconds = 15 }
};

try
{
    var created = new DateTimeOffset(2024, 3, 9, 8, 7, 6, TimeSpan.Zero);
    var assetId = Guid.NewGuid();
    var database = new DemoSqliteDatabase(settings);
    await database.EnsureInitializedAsync();
    await ExecuteAsync(database,
        """
        INSERT INTO DemoAsset(AssetId,Title,Lifecycle,Version,EventDate,Category,TagsJson,CreatedAtUtc,UpdatedAtUtc,OriginalFileName,MediaKind)
        VALUES($id,'Legacy Cabinet Footage','Draft',1,NULL,'Archive','[]',$created,$created,'cabinet.mp4','Video');
        """,
        ("$id", assetId.ToString("D")), ("$created", DemoSqliteDatabase.ToDb(created)));

    var audit = new InMemoryAuditSink();
    var store = new MediaLibraryOrganizationStore(null, database, audit);
    var initial = (await store.ListAsync()).Single(x => x.AssetId == assetId);
    Require(initial.UploadedAtUtc == created, "Upload date must backfill from the authoritative historical CreatedAtUtc value.");
    Require(initial.ProductionDate is null, "Actual production date must stay NULL when it was not supplied.");
    Require(initial.CategoryNameEn == "Archive", "Legacy category text must be preserved into the structured category catalog.");
    Require(initial.CategoryId != MediaLibraryOrganizationStore.UncategorizedCategoryId, "A non-empty legacy category must not be discarded into Uncategorized.");

    var newsId = Guid.NewGuid();
    var now = DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow);
    await ExecuteAsync(database,
        """
        INSERT INTO DemoCategory(CategoryId,ParentCategoryId,NameEn,NameAr,IsSystem,SortOrder,Version,CreatedAtUtc,UpdatedAtUtc)
        VALUES($id,NULL,'News','أخبار',0,10,1,$now,$now);
        """, ("$id", newsId.ToString("D")), ("$now", now));

    var productionDate = new DateOnly(2023, 12, 24);
    var changed = await store.UpdateAsync(assetId, new UpdateMediaOrganizationRequest(initial.Version, productionDate, newsId), "demo-editor");
    Require(changed.Version == initial.Version + 1, "Successful organization mutation must increment the asset version.");
    Require(changed.ProductionDate == productionDate, "Actual production date must persist exactly as supplied.");
    Require(changed.CategoryId == newsId && changed.CategoryNameEn == "News", "Structured category assignment must persist and reread authoritatively.");
    Require(changed.UploadedAtUtc == created, "Organization editing must never mutate the immutable upload date.");

    await using (var connection = await database.OpenAsync())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT Category FROM DemoAsset WHERE AssetId=$id;";
        command.Parameters.AddWithValue("$id", assetId.ToString("D"));
        Require((string?)await command.ExecuteScalarAsync() == "News", "Legacy category compatibility value must stay synchronized.");
    }

    try
    {
        await store.UpdateAsync(assetId, new UpdateMediaOrganizationRequest(initial.Version, null, newsId), "stale-editor");
        throw new InvalidOperationException("A stale ExpectedVersion unexpectedly succeeded.");
    }
    catch (MediaLibraryRequestException ex)
    {
        Require(ex.StatusCode == 409 && ex.Code == "concurrency_conflict" && ex.Current?.Version == changed.Version,
            "Stale mutations must fail closed with the current authoritative version.");
    }

    try
    {
        await store.UpdateAsync(assetId, new UpdateMediaOrganizationRequest(changed.Version, null, Guid.NewGuid()), "demo-editor");
        throw new InvalidOperationException("An unknown category unexpectedly succeeded.");
    }
    catch (MediaLibraryRequestException ex)
    {
        Require(ex.StatusCode == 400 && ex.Code == "category_not_found", "Unknown categories must be rejected.");
    }

    var cleared = await store.UpdateAsync(assetId,
        new UpdateMediaOrganizationRequest(changed.Version, null, MediaLibraryOrganizationStore.UncategorizedCategoryId), "demo-editor");
    Require(cleared.ProductionDate is null, "Clearing Actual Production Date must persist NULL.");
    Require(cleared.CategoryId == MediaLibraryOrganizationStore.UncategorizedCategoryId && cleared.CategoryNameAr == "غير مصنف",
        "The protected bilingual Uncategorized category must be a valid stable fallback.");
    Require(cleared.UploadedAtUtc == created, "Clearing organization fields must not change upload date.");

    var newAssetId = Guid.NewGuid();
    var newCreated = DateTimeOffset.UtcNow.AddMinutes(-2);
    await ExecuteAsync(database,
        """
        INSERT INTO DemoAsset(AssetId,Title,Lifecycle,Version,Category,TagsJson,CreatedAtUtc,UpdatedAtUtc,OriginalFileName,MediaKind)
        VALUES($id,'New Unclassified Asset','Draft',1,NULL,'[]',$created,$created,'photo.jpg','Image');
        """, ("$id", newAssetId.ToString("D")), ("$created", DemoSqliteDatabase.ToDb(newCreated)));
    var newAsset = await store.GetAsync(newAssetId);
    Require(newAsset.UploadedAtUtc == newCreated, "New Demo assets must receive the system upload date from creation time.");
    Require(newAsset.CategoryId == MediaLibraryOrganizationStore.UncategorizedCategoryId,
        "New unclassified Demo assets must default to Uncategorized.");

    var events = await audit.ListRecentAsync(20);
    Require(events.Count(x => x.Action == "asset.organization.updated" && x.EntityId == assetId.ToString("D")) == 2,
        "Only successful organization mutations must emit success audit evidence.");
    Require(events.All(x => !string.IsNullOrWhiteSpace(x.ActorId)), "Audit evidence must retain the actor identity.");

    var reopenedDatabase = new DemoSqliteDatabase(settings);
    var reopenedStore = new MediaLibraryOrganizationStore(null, reopenedDatabase, new InMemoryAuditSink());
    var persisted = await reopenedStore.GetAsync(assetId);
    Require(persisted.ProductionDate is null && persisted.CategoryId == MediaLibraryOrganizationStore.UncategorizedCategoryId,
        "Organization values must survive a fresh Offline Demo database/provider instance.");
    Require(persisted.UploadedAtUtc == created, "Historical upload date must remain stable after restart/reopen.");

    Console.WriteLine("P133 MEDIA LIBRARY ACCEPTANCE: PASS");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}
