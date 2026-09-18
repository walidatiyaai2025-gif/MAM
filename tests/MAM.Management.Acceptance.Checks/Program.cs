using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Curation;
using MAM.Application.Discovery;
using MAM.Application.Metadata;
using MAM.Domain.Assets;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Curation;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Discovery;
using Microsoft.Data.SqlClient;

if (args.Length >= 3)
{
    var masterConnectionString = args[0];
    var databaseName = args[1];
    var migrationDirectory = Path.GetFullPath(args[2]);
    if (databaseName.Length is < 1 or > 80 || databaseName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
        throw new InvalidOperationException("Acceptance database name contains unsupported characters.");

    await WaitForSqlServerAsync(masterConnectionString);
    await RecreateDatabaseAsync(masterConnectionString, databaseName);
    var targetBuilder = new SqlConnectionStringBuilder(masterConnectionString) { InitialCatalog = databaseName };
    var connections = new SqlServerConnectionFactory(targetBuilder.ConnectionString, 30, enableRetryOnFailure: true);
    var migrations = new SqlServerMigrationRunner(connections);
    var firstPass = await migrations.ApplyDirectoryAsync(migrationDirectory);
    var secondPass = await migrations.ApplyDirectoryAsync(migrationDirectory);

    Require(firstPass.Contains("0016_taxonomy_collections_tags_management.sql", StringComparer.Ordinal),
        "SQL migration 0016 is applied");
    Equal(firstPass.Count, secondPass.Count, "SQL migrations remain idempotent");

    IAuditSink audit = new SqlServerAuditSink(connections);
    IAssetCatalog catalog = new SqlServerAssetCatalog(connections, audit);
    ICurationService curation = new SqlServerCurationService(connections, audit, new BuiltInMetadataSchemaRegistry());
    IDiscoveryService discovery = new SqlServerDiscoveryService(connections, audit);

    await RunManagementSuiteAsync(curation, catalog, discovery, audit, "SQL Server");

    await using (var connection = await connections.OpenAsync())
    await using (var command = new SqlCommand(
        "SELECT COUNT(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0016_taxonomy_collections_tags_management';", connection))
    {
        Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()),
            "SQL schema version records migration 0016 exactly once");
    }

    Console.WriteLine("MANAGEMENT SQL SERVER ACCEPTANCE: PASS");
    return 0;
}

var root = Path.Combine(Path.GetTempPath(), "mam-management-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var settings = new MamSettings
    {
        Environment = new EnvironmentSettings { Name = "Demo" },
        Database = new DatabaseSettings
        {
            Provider = "Sqlite",
            SqlitePath = Path.Combine(root, "management.db"),
            CommandTimeoutSeconds = 10
        }
    };
    var db = new DemoSqliteDatabase(settings);
    await db.EnsureInitializedAsync();
    IAuditSink audit = new InMemoryAuditSink();
    IAssetCatalog catalog = new DemoSqliteAssetCatalog(db, audit);
    ICurationService curation = new DemoCurationService(db, audit);
    IDiscoveryService discovery = new DemoDiscoveryService(db, audit);

    await RunManagementSuiteAsync(curation, catalog, discovery, audit, "Demo SQLite");
    RunClientContractChecks();

    Console.WriteLine("MANAGEMENT DEMO + CLIENT CONTRACT ACCEPTANCE: PASS");
    return 0;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static async Task RunManagementSuiteAsync(
    ICurationService curation,
    IAssetCatalog catalog,
    IDiscoveryService discovery,
    IAuditSink audit,
    string provider)
{
    var createdAsset = await catalog.CreateAsync("Management acceptance asset", "management-acceptance");
    Require(createdAsset.Status == CatalogMutationStatus.Created && createdAsset.Asset is not null,
        $"{provider}: test asset is created");
    var assetId = createdAsset.Asset!.Id;

    // Categories: create, update, safe-delete and in-use guard.
    var category = await discovery.CreateCategoryAsync(
        new CreateCategoryRequest(null, "Management Category", "تصنيف الإدارة", 10),
        "management-acceptance");
    Equal("Management Category", category.NameEn, $"{provider}: category create");

    var updatedCategory = await discovery.UpdateCategoryAsync(
        category.CategoryId,
        new UpdateCategoryRequest(category.Version, null, "Management Category Renamed", "تصنيف الإدارة المعدل", 20),
        "management-acceptance");
    Equal(category.Version + 1, updatedCategory.Version, $"{provider}: category optimistic version increments");
    Equal("Management Category Renamed", updatedCategory.NameEn, $"{provider}: category rename persists");

    await discovery.AssignAssetCategoryAsync(assetId, updatedCategory.CategoryId, "management-acceptance");
    var categoryBlocked = false;
    try
    {
        await discovery.DeleteCategoryAsync(updatedCategory.CategoryId, "management-acceptance");
    }
    catch (DiscoveryRequestException ex) when (ex.StatusCode == 409)
    {
        categoryBlocked = true;
    }
    Require(categoryBlocked, $"{provider}: category deletion fails closed while assets use it");

    await discovery.AssignAssetCategoryAsync(assetId, null, "management-acceptance");
    await discovery.DeleteCategoryAsync(updatedCategory.CategoryId, "management-acceptance");
    Require((await discovery.ListCategoriesAsync()).All(x => x.CategoryId != updatedCategory.CategoryId),
        $"{provider}: empty non-system category deletes");

    // Collections: create, duplicate guard, rename, membership and delete definition only.
    var collection = await curation.CreateCollectionAsync(
        new CreateCollectionRequest("Management Collection", "مجموعة الإدارة"),
        "management-acceptance");
    Equal(1L, collection.Version, $"{provider}: collection starts at version 1");

    var duplicateCollectionBlocked = false;
    try
    {
        _ = await curation.CreateCollectionAsync(
            new CreateCollectionRequest("Management Collection", null),
            "management-acceptance");
    }
    catch (CurationRequestException ex) when (ex.Code == "collection_duplicate" && ex.StatusCode == 409)
    {
        duplicateCollectionBlocked = true;
    }
    Require(duplicateCollectionBlocked, $"{provider}: duplicate collection name is rejected");

    var renamedCollection = await curation.UpdateCollectionAsync(
        collection.CollectionId,
        new UpdateCollectionRequest(collection.Version, "Management Collection Renamed", "مجموعة الإدارة المعدلة"),
        "management-acceptance");
    Equal(2L, renamedCollection.Version, $"{provider}: collection rename increments version");

    var withMember = await curation.AddToCollectionAsync(
        renamedCollection.CollectionId,
        assetId,
        renamedCollection.Version,
        "management-acceptance");
    Equal(1, withMember.MemberCount, $"{provider}: collection member is added");
    Equal(3L, withMember.Version, $"{provider}: membership mutation increments collection version");

    var staleCollectionRejected = false;
    try
    {
        _ = await curation.UpdateCollectionAsync(
            withMember.CollectionId,
            new UpdateCollectionRequest(renamedCollection.Version, "Stale name", null),
            "management-acceptance");
    }
    catch (CurationRequestException ex) when (ex.StatusCode == 409)
    {
        staleCollectionRejected = true;
    }
    Require(staleCollectionRejected, $"{provider}: stale collection edit fails closed");

    await curation.DeleteCollectionAsync(withMember.CollectionId, withMember.Version, "management-acceptance");
    Require((await curation.ListCollectionsAsync()).All(x => x.CollectionId != withMember.CollectionId),
        $"{provider}: collection definition deletes");
    Require(await catalog.GetAsync(new AssetId(assetId)) is not null,
        $"{provider}: deleting collection never deletes member asset");

    // Tags: dictionary CRUD, asset usage counts, propagated rename and explicit safe removal.
    var tag = await curation.CreateTagAsync(new CreateTagRequest("VIP"), "management-acceptance");
    Equal(0, tag.AssetCount, $"{provider}: new dictionary tag starts unused");

    var metadata = await curation.GetMetadataAsync(assetId)
        ?? throw new InvalidOperationException($"{provider}: metadata snapshot missing for created asset");
    var tagged = await curation.UpdateMetadataAsync(
        assetId,
        new AssetMetadataUpdateRequest(
            metadata.Version,
            BuiltInMetadataSchemaRegistry.CoreMediaSchemaKey,
            metadata.TitleEn,
            metadata.TitleAr,
            metadata.EventDate,
            metadata.Category,
            new[] { "VIP" },
            metadata.PreservationNotes),
        "management-acceptance");
    Require(tagged.Tags.SequenceEqual(new[] { "VIP" }, StringComparer.Ordinal),
        $"{provider}: metadata uses canonical dictionary tag");

    var usedTag = (await curation.ListTagsAsync()).Single(x => x.TagId == tag.TagId);
    Equal(1, usedTag.AssetCount, $"{provider}: tag usage count reflects assigned asset");

    var renamedTag = await curation.UpdateTagAsync(
        tag.TagId,
        new UpdateTagRequest(usedTag.Version, "Royal VIP"),
        "management-acceptance");
    Equal(usedTag.Version + 1, renamedTag.Version, $"{provider}: tag rename increments tag version");

    var afterRename = await curation.GetMetadataAsync(assetId)
        ?? throw new InvalidOperationException($"{provider}: metadata missing after tag rename");
    Require(afterRename.Tags.SequenceEqual(new[] { "Royal VIP" }, StringComparer.Ordinal),
        $"{provider}: tag rename propagates to assigned asset metadata");
    Equal(tagged.Version + 1, afterRename.Version,
        $"{provider}: tag propagation bumps asset version to protect stale editors");

    var staleMetadataRejected = false;
    try
    {
        _ = await curation.UpdateMetadataAsync(
            assetId,
            new AssetMetadataUpdateRequest(
                tagged.Version,
                BuiltInMetadataSchemaRegistry.CoreMediaSchemaKey,
                tagged.TitleEn,
                tagged.TitleAr,
                tagged.EventDate,
                tagged.Category,
                tagged.Tags,
                tagged.PreservationNotes),
            "stale-management-editor");
    }
    catch (CurationRequestException ex) when (ex.StatusCode == 409)
    {
        staleMetadataRejected = true;
    }
    Require(staleMetadataRejected, $"{provider}: stale metadata write is rejected after propagated tag rename");

    var inUseDeleteBlocked = false;
    try
    {
        _ = await curation.DeleteTagAsync(
            renamedTag.TagId,
            renamedTag.Version,
            removeFromAssets: false,
            "management-acceptance");
    }
    catch (CurationRequestException ex) when (ex.Code == "tag_in_use" && ex.StatusCode == 409)
    {
        inUseDeleteBlocked = true;
    }
    Require(inUseDeleteBlocked, $"{provider}: deleting an in-use tag requires explicit detach confirmation");

    var deletion = await curation.DeleteTagAsync(
        renamedTag.TagId,
        renamedTag.Version,
        removeFromAssets: true,
        "management-acceptance");
    Equal(1, deletion.RemovedFromAssets, $"{provider}: confirmed tag deletion reports detached asset count");
    Require((await curation.ListTagsAsync()).All(x => x.TagId != renamedTag.TagId),
        $"{provider}: tag dictionary entry is deleted");

    var afterDelete = await curation.GetMetadataAsync(assetId)
        ?? throw new InvalidOperationException($"{provider}: metadata missing after tag delete");
    Equal(0, afterDelete.Tags.Count, $"{provider}: confirmed tag delete removes tag from asset");
    Equal(afterRename.Version + 1, afterDelete.Version,
        $"{provider}: tag deletion propagation bumps asset version");

    // Free-form metadata entry remains backwards compatible by creating a canonical dictionary row.
    var autoTagMetadata = await curation.UpdateMetadataAsync(
        assetId,
        new AssetMetadataUpdateRequest(
            afterDelete.Version,
            BuiltInMetadataSchemaRegistry.CoreMediaSchemaKey,
            afterDelete.TitleEn,
            afterDelete.TitleAr,
            afterDelete.EventDate,
            afterDelete.Category,
            new[] { "Archive Review" },
            afterDelete.PreservationNotes),
        "management-acceptance");
    var autoTag = (await curation.ListTagsAsync()).Single(x => x.NormalizedName == CurationTextNormalizer.NormalizeSearch("Archive Review"));
    Equal(1, autoTag.AssetCount, $"{provider}: legacy/free-form tag entry is promoted into the authoritative dictionary");
    Require(autoTagMetadata.Tags.Contains(autoTag.Name, StringComparer.Ordinal),
        $"{provider}: asset stores canonical dictionary display name");

    var events = await audit.ListRecentAsync(500);
    Require(events.Any(x => x.Action.Contains("collection", StringComparison.OrdinalIgnoreCase)),
        $"{provider}: collection mutations are audited");
    Require(events.Any(x => x.Action.Contains("tag", StringComparison.OrdinalIgnoreCase)),
        $"{provider}: tag mutations are audited");
}

static void RunClientContractChecks()
{
    var web = File.ReadAllText("src/MAM.Web/wwwroot/p138-taxonomy-management.js");
    var index = File.ReadAllText("src/MAM.Web/wwwroot/index.html");
    var desktop = File.ReadAllText("src/MAM.Desktop/MainWindow.Management.cs");
    var p12 = File.ReadAllText("src/MAM.Desktop/MainWindow.P12.cs");
    var api = File.ReadAllText("src/MAM.Api/Program.cs");
    var migration = File.ReadAllText("database/migrations/0016_taxonomy_collections_tags_management.sql");

    foreach (var marker in new[]
    {
        "Collection Management",
        "إدارة المجموعات",
        "Tag Management",
        "إدارة الوسوم",
        "removeFromAssets",
        "Membership management",
        "/client-api/curation/tags",
        "/client-api/curation/collections/"
    })
        Require(web.Contains(marker, StringComparison.Ordinal), "Web management marker: " + marker);

    Require(index.Contains("/p138-taxonomy-management.js", StringComparison.Ordinal),
        "Web runtime composes taxonomy management pages");

    foreach (var marker in new[]
    {
        "LoadManagementCollectionsAsync",
        "LoadManagementTagsAsync",
        "ManagementCollectionDialog",
        "ManagementTagDialog",
        "DeleteCollectionAsync",
        "DeleteTagAsync",
        "لن يتم حذف أي أصل إعلامي"
    })
        Require(desktop.Contains(marker, StringComparison.Ordinal), "Desktop management marker: " + marker);

    Require(p12.Contains(""collections" => LoadManagementCollectionsAsync()", StringComparison.Ordinal) &&
            p12.Contains(""tags" => LoadManagementTagsAsync()", StringComparison.Ordinal),
        "Desktop navigation composes collection and tag pages");

    Require(!web.Contains("SqlConnection", StringComparison.Ordinal) &&
            !desktop.Contains("SqlConnection", StringComparison.Ordinal),
        "Web/Desktop management surfaces remain Central-API-only");

    foreach (var endpoint in new[]
    {
        "/curation/collections/{collectionId:guid}",
        "/curation/tags",
        "/curation/tags/{tagId:guid}"
    })
        Require(api.Contains(endpoint, StringComparison.Ordinal), "Central API management route: " + endpoint);

    Require(migration.Contains("CREATE TABLE dbo.MamTag", StringComparison.Ordinal) &&
            migration.Contains("UQ_MamTag_NormalizedName", StringComparison.Ordinal) &&
            migration.Contains("0016_taxonomy_collections_tags_management", StringComparison.Ordinal),
        "SQL migration contains authoritative tag dictionary and schema version marker");
}

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"FAILED: {name}. Expected={expected}; Actual={actual}");
    Console.WriteLine("PASS: " + name);
}

static async Task WaitForSqlServerAsync(string connectionString)
{
    Exception? last = null;
    for (var attempt = 1; attempt <= 60; attempt++)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT 1;", connection) { CommandTimeout = 5 };
            await command.ExecuteScalarAsync();
            return;
        }
        catch (Exception ex) when (ex is SqlException or TimeoutException or InvalidOperationException)
        {
            last = ex;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    throw new InvalidOperationException("Ephemeral SQL Server did not become ready within 60 seconds.", last);
}

static async Task RecreateDatabaseAsync(string masterConnectionString, string databaseName)
{
    await using var connection = new SqlConnection(masterConnectionString);
    await connection.OpenAsync();
    var escaped = databaseName.Replace("]", "]]", StringComparison.Ordinal);
    var literal = databaseName.Replace("'", "''", StringComparison.Ordinal);
    var sql = $"""
        IF DB_ID(N'{literal}') IS NOT NULL
        BEGIN
            ALTER DATABASE [{escaped}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{escaped}];
        END;
        CREATE DATABASE [{escaped}];
        """;
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
    await command.ExecuteNonQueryAsync();
}
