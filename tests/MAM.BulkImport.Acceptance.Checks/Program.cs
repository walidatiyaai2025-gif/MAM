using System.Security.Cryptography;
using System.Text;
using MAM.Application.Auditing;
using MAM.Application.BulkImport;
using MAM.Application.Catalog;
using MAM.Application.Discovery;
using MAM.Application.Uploads;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.BulkImport;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Storage;

var root = Path.Combine(Path.GetTempPath(), "mam-bulk-import-" + Guid.NewGuid().ToString("N"));
var primaryRoot = Path.Combine(root, "primary");
var backupRoot = Path.Combine(root, "backup");
Directory.CreateDirectory(primaryRoot);
Directory.CreateDirectory(backupRoot);

try
{
    var settings = new MamSettings
    {
        Environment = new EnvironmentSettings { Name = "Demo" },
        Database = new DatabaseSettings
        {
            Provider = "Sqlite",
            SqlitePath = Path.Combine(root, "bulk.db"),
            CommandTimeoutSeconds = 10
        },
        Storage = new StorageSettings
        {
            Primary = new PrimaryStorageTargetSettings
            {
                Id = "primary",
                Type = "Mock",
                Root = primaryRoot,
                OriginalsPrefix = "originals",
                DerivativesPrefix = "derivatives",
                MinimumFreeGB = 0,
                MinimumFreePercent = 0,
                WriteTestOnHealthCheck = false
            },
            Backup = new BackupStorageTargetSettings
            {
                Id = "backup",
                Type = "FileSystem",
                Root = backupRoot,
                MinimumFreeGB = 0,
                CopyOriginals = true,
                VerifyChecksum = true
            }
        },
        Upload = new UploadSettings
        {
            ChunkSizeMB = 1,
            MaxConcurrentFilesPerClient = 2,
            MaxFileSizeGB = 2,
            ResumeEnabled = true,
            SessionExpiryHours = 24,
            ChecksumAlgorithm = "SHA-256",
            DuplicatePolicy = "Reject",
            WebEnabled = true,
            WebMaxConcurrentFiles = 1,
            AllowedExtensions = [".mp4", ".txt", ".jpg"],
            QuarantineUnknownFiles = false
        }
    };

    var db = new DemoSqliteDatabase(settings);
    await db.EnsureInitializedAsync();
    IAuditSink audit = new InMemoryAuditSink();
    IAssetCatalog catalog = new DemoSqliteAssetCatalog(db, audit);
    var primary = new FileSystemStorageObjectStore(settings.Storage.Primary);
    IDurableUploadService uploads = new DemoDurableUploadService(db, catalog, primary, audit, settings);
    IDiscoveryService discovery = new DemoDiscoveryService(db, audit);
    var processing = new DemoMediaProcessingService(db, primary);
    IBulkImportStateStore store = new DemoBulkImportStateStore(db);
    var coordinator = new BulkImportCoordinator(store, uploads, discovery, processing, audit, settings);

    var eventBytes = new byte[1_572_864];
    for (var i = 0; i < eventBytes.Length; i++) eventBytes[i] = (byte)((i * 31 + 7) % 251);
    var monthTwoBytes = Encoding.UTF8.GetBytes("MAM bulk import 2026 month 2 acceptance.");
    var rootBytes = Encoding.UTF8.GetBytes("MAM bulk import root-level document acceptance.");
    var eventSha = Sha(eventBytes);
    var monthTwoSha = Sha(monthTwoBytes);
    var rootSha = Sha(rootBytes);

    var created = await coordinator.CreateSessionAsync(
        new CreateBulkImportSessionRequest("2026",
        [
            new BulkImportFileDescriptor("1/event.mp4", eventBytes.LongLength, eventSha),
            new BulkImportFileDescriptor("2/month-two.txt", monthTwoBytes.LongLength, monthTwoSha),
            new BulkImportFileDescriptor("root.txt", rootBytes.LongLength, rootSha),
            new BulkImportFileDescriptor("1/ignore.exe", 7, Sha(Encoding.UTF8.GetBytes("ignore!")))
        ]),
        "acceptance-user");

    Equal(4, created.TotalFiles, "manifest keeps all selected files");
    Equal(1, created.Unsupported, "unsupported files are terminal and reported");
    Equal("1", created.Items.Single(x => x.RelativePath == "1/event.mp4").CategoryName,
        "first child folder becomes the target subcategory");
    Equal("2", created.Items.Single(x => x.RelativePath == "2/month-two.txt").CategoryName,
        "second child folder becomes a sibling target subcategory");
    Equal("2026", created.Items.Single(x => x.RelativePath == "root.txt").CategoryName,
        "root-level media uses the selected root category");

    var categories = await discovery.ListCategoriesAsync();
    var yearCategory = categories.Single(x => x.ParentCategoryId is null && x.NameEn == "2026");
    var monthOneCategory = categories.Single(x => x.ParentCategoryId == yearCategory.CategoryId && x.NameEn == "1");
    var monthTwoCategory = categories.Single(x => x.ParentCategoryId == yearCategory.CategoryId && x.NameEn == "2");
    Require(!categories.Any(x => x.ParentCategoryId is null && (x.NameEn == "1" || x.NameEn == "2")),
        "child folders are not created as independent root categories");
    Require(monthOneCategory.ParentCategoryId == yearCategory.CategoryId &&
            monthTwoCategory.ParentCategoryId == yearCategory.CategoryId,
        "selected root folder is the parent category for immediate child folders");

    var eventItem = created.Items.Single(x => x.RelativePath == "1/event.mp4");
    var begun = await coordinator.BeginItemAsync(created.SessionId, eventItem.ItemId, "acceptance-user");
    Equal(BulkImportItemState.Uploading, begun.State, "begin moves pending item to uploading");
    Require(begun.UploadSessionId is Guid, "bulk item owns a standard durable upload session");

    var uploadId = begun.UploadSessionId!.Value;
    var firstChunk = eventBytes.AsMemory(0, 512 * 1024).ToArray();
    await using (var firstStream = new MemoryStream(firstChunk, writable: false))
        await uploads.PutChunkAsync(uploadId, 0, Sha(firstChunk), firstStream, "acceptance-user");

    var reconstructed = new BulkImportCoordinator(
        new DemoBulkImportStateStore(db), uploads, discovery, processing, audit, settings);
    var resumed = await reconstructed.BeginItemAsync(created.SessionId, eventItem.ItemId, "acceptance-user");
    Equal(512L * 1024L, resumed.ReceivedLength,
        "service reconstruction resumes from authoritative acknowledged upload offset");

    await UploadRemainderAsync(uploads, uploadId, eventBytes, "acceptance-user");
    var finalizedEvent = await reconstructed.FinalizeItemAsync(
        created.SessionId, eventItem.ItemId, "acceptance-user");
    Equal(BulkImportItemState.Uploaded, finalizedEvent.State, "uploaded media reaches terminal Uploaded state");
    Require(finalizedEvent.AssetId is Guid, "uploaded media exposes authoritative asset id");
    var eventAssetId = finalizedEvent.AssetId!.Value;
    var eventCategory = await discovery.GetAssetCategoryAsync(eventAssetId);
    Equal("1", eventCategory.Category.NameEn, "uploaded asset receives immediate child-folder subcategory");
    Equal(yearCategory.CategoryId, eventCategory.Category.ParentCategoryId!.Value,
        "uploaded child-folder media is attached beneath the selected root category");

    var monthTwoItem = (await reconstructed.GetSessionAsync(created.SessionId, "acceptance-user"))
        .Items.Single(x => x.RelativePath == "2/month-two.txt");
    var monthTwoBegin = await reconstructed.BeginItemAsync(created.SessionId, monthTwoItem.ItemId, "acceptance-user");
    await UploadRemainderAsync(uploads, monthTwoBegin.UploadSessionId!.Value, monthTwoBytes, "acceptance-user");
    var finalizedMonthTwo = await reconstructed.FinalizeItemAsync(
        created.SessionId, monthTwoItem.ItemId, "acceptance-user");
    Equal(BulkImportItemState.Uploaded, finalizedMonthTwo.State, "second subcategory media uploads successfully");
    var monthTwoAssetCategory = await discovery.GetAssetCategoryAsync(finalizedMonthTwo.AssetId!.Value);
    Equal("2", monthTwoAssetCategory.Category.NameEn, "second child-folder media receives subcategory 2");
    Equal(yearCategory.CategoryId, monthTwoAssetCategory.Category.ParentCategoryId!.Value,
        "subcategory 2 remains beneath root category 2026");

    var rootItem = (await reconstructed.GetSessionAsync(created.SessionId, "acceptance-user"))
        .Items.Single(x => x.RelativePath == "root.txt");
    var rootBegin = await reconstructed.BeginItemAsync(created.SessionId, rootItem.ItemId, "acceptance-user");
    await UploadRemainderAsync(uploads, rootBegin.UploadSessionId!.Value, rootBytes, "acceptance-user");
    var finalizedRoot = await reconstructed.FinalizeItemAsync(
        created.SessionId, rootItem.ItemId, "acceptance-user");
    Equal(BulkImportItemState.Uploaded, finalizedRoot.State, "root-level supported file uploads successfully");
    var rootCategory = await discovery.GetAssetCategoryAsync(finalizedRoot.AssetId!.Value);
    Equal("2026", rootCategory.Category.NameEn, "root-level asset receives selected root category");
    Require(rootCategory.Category.ParentCategoryId is null, "selected root folder remains a root category");

    var complete = await reconstructed.GetSessionAsync(created.SessionId, "acceptance-user");
    Equal(BulkImportSessionState.CompletedWithErrors, complete.State,
        "unsupported item yields truthful completed-with-errors session");
    Equal(4, complete.ProcessedFiles, "all manifest items become terminal");
    Equal(3, complete.Uploaded, "three supported originals upload exactly once");
    Equal(1, complete.Unsupported, "unsupported count remains explicit");

    var jobs = await processing.ListJobsAsync(100);
    Require(jobs.Any(x => x.AssetId == eventAssetId && x.ProfileId == "inspect-v1"),
        "bulk upload queues technical inspection");
    Require(jobs.Any(x => x.AssetId == eventAssetId && x.ProfileId == "transcript-text-v1"),
        "bulk video upload queues transcription");

    var textReport = await reconstructed.GetReportAsync(created.SessionId, "txt", "acceptance-user");
    Require(textReport.Content.Contains("[UNSUPPORTED]", StringComparison.Ordinal) &&
            textReport.Content.Contains("1/ignore.exe", StringComparison.Ordinal) &&
            textReport.Content.Contains("2026", StringComparison.Ordinal),
        "TXT report includes unsupported reason and category detail");
    var csvReport = await reconstructed.GetReportAsync(created.SessionId, "csv", "acceptance-user");
    Require(csvReport.Content.StartsWith("relative_path,file_name,category,state", StringComparison.Ordinal) &&
            csvReport.Content.Contains("\"1/event.mp4\"", StringComparison.Ordinal) &&
            csvReport.Content.Contains("\"2/month-two.txt\"", StringComparison.Ordinal),
        "CSV report contains stable headers and quoted file rows");

    var duplicate = await reconstructed.CreateSessionAsync(
        new CreateBulkImportSessionRequest("Second Import",
        [
            new BulkImportFileDescriptor("Copies/event-copy.mp4", eventBytes.LongLength, eventSha)
        ]),
        "acceptance-user");
    var linked = duplicate.Items.Single();
    Equal(BulkImportItemState.Linked, linked.State,
        "duplicate content in another folder is not re-uploaded and is reclassified");
    Equal(eventAssetId, linked.AssetId!.Value, "duplicate reuses existing authoritative asset identity");
    Require(linked.UploadSessionId is null, "duplicate preflight does not create a second upload session");
    var reclassified = await discovery.GetAssetCategoryAsync(eventAssetId);
    Equal("Copies", reclassified.Category.NameEn, "duplicate asset category changes to folder-derived subcategory");
    var afterDuplicateCategories = await discovery.ListCategoriesAsync();
    var secondImportRoot = afterDuplicateCategories.Single(x => x.ParentCategoryId is null && x.NameEn == "Second Import");
    Equal(secondImportRoot.CategoryId, reclassified.Category.ParentCategoryId!.Value,
        "duplicate reclassification targets the child category beneath the selected root");

    var sameCategoryDuplicate = await reconstructed.CreateSessionAsync(
        new CreateBulkImportSessionRequest("Second Import",
        [
            new BulkImportFileDescriptor("Copies/again.mp4", eventBytes.LongLength, eventSha)
        ]),
        "acceptance-user");
    Equal(BulkImportItemState.AlreadyExists, sameCategoryDuplicate.Items.Single().State,
        "duplicate already in target category is skipped without mutation");

    var assets = await catalog.ListAsync();
    Equal(3, assets.Count, "duplicate sessions do not create duplicate catalog assets");

    var ownershipRejected = false;
    try
    {
        _ = await reconstructed.GetSessionAsync(created.SessionId, "different-user");
    }
    catch (BulkImportRequestException ex) when (ex.Code == "bulk_import_forbidden" && ex.StatusCode == 403)
    {
        ownershipRejected = true;
    }
    Require(ownershipRejected, "bulk sessions are fail-closed to the creating user");

    var reopened = new BulkImportCoordinator(
        new DemoBulkImportStateStore(new DemoSqliteDatabase(settings)),
        uploads, discovery, processing, audit, settings);
    var persisted = await reopened.GetSessionAsync(created.SessionId, "acceptance-user");
    Equal(complete.ProcessedFiles, persisted.ProcessedFiles,
        "bulk session state persists across state-store reconstruction");

    RunClientContractChecks();

    Console.WriteLine("BULK FOLDER IMPORT DEMO + CLIENT CONTRACT ACCEPTANCE: PASS");
    return 0;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static async Task UploadRemainderAsync(
    IDurableUploadService uploads,
    Guid sessionId,
    byte[] bytes,
    string actor)
{
    var snapshot = await uploads.GetSessionAsync(sessionId);
    var offset = snapshot.ReceivedLength;
    var chunkSize = snapshot.Session.ChunkSizeBytes;
    while (offset < bytes.LongLength)
    {
        var count = (int)Math.Min(chunkSize, bytes.LongLength - offset);
        var chunk = bytes.AsMemory((int)offset, count).ToArray();
        await using var stream = new MemoryStream(chunk, writable: false);
        var receipt = await uploads.PutChunkAsync(sessionId, offset, Sha(chunk), stream, actor);
        offset = receipt.ReceivedLength;
    }
}

static string Sha(byte[] bytes) =>
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static void RunClientContractChecks()
{
    var web = File.ReadAllText("src/MAM.Web/wwwroot/p137-bulk-folder-import.js");
    var index = File.ReadAllText("src/MAM.Web/wwwroot/index.html");
    var desktop = File.ReadAllText("src/MAM.Desktop/MainWindow.BulkImport.cs");
    var api = File.ReadAllText("src/MAM.Api/BulkImportEndpoints.cs");
    var migration = File.ReadAllText("database/migrations/0015_bulk_folder_import.sql");

    foreach (var marker in new[]
    {
        "webkitdirectory",
        "P137Sha256",
        "/client-api/bulk-import/sessions",
        "p137OverallBar",
        "TXT report",
        "استيراد مجلدات مجمّع",
        "The selected folder is the main category",
        "المجلد المختار هو التصنيف الرئيسي",
        "file.slice(offset,end).arrayBuffer()"
    })
        Require(web.Contains(marker, StringComparison.Ordinal), "Web bulk-import marker: " + marker);

    Require(index.Contains("/p137-bulk-folder-import.js", StringComparison.Ordinal),
        "Web runtime composes the bulk folder workspace");

    foreach (var marker in new[]
    {
        "OpenFolderDialog",
        "ProgressBar",
        "SaveBulkReportsAsync",
        "BuildBulkLocalManifestAsync",
        "استيراد مجلدات مجمّع",
        "The selected folder is the main category",
        "المجلد المختار هو التصنيف الرئيسي",
        "Import Reports"
    })
        Require(desktop.Contains(marker, StringComparison.Ordinal), "Desktop bulk-import marker: " + marker);

    Require(!web.Contains("SqlConnection", StringComparison.Ordinal) &&
            !desktop.Contains("SqlConnection", StringComparison.Ordinal),
        "Web/Desktop bulk-import clients stay Central-API-only");

    var readPolicies = Count(api, ".RequireAuthorization(MamSecurity.CatalogReadPolicy);");
    var writePolicies = Count(api, ".RequireAuthorization(MamSecurity.CatalogWritePolicy);");
    Require(readPolicies >= 3 && writePolicies >= 6,
        "Bulk API routes are protected by catalog read/write authorization");

    Require(migration.Contains("MamBulkImportSession", StringComparison.Ordinal) &&
            migration.Contains("MamBulkImportItem", StringComparison.Ordinal) &&
            migration.Contains("0015_bulk_folder_import", StringComparison.Ordinal),
        "SQL migration contains durable bulk session/item schema and version marker");
}

static int Count(string text, string needle)
{
    var count = 0;
    for (var index = 0; (index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
        count++;
    return count;
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
