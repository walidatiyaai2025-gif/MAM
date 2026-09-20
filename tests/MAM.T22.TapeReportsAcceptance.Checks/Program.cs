using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Application.SystemFunctions;
using MAM.Application.Tapes;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.SystemFunctions;
using MAM.Infrastructure.Tapes;

var root=Path.Combine(Path.GetTempPath(),"mam-t22-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var settings=new MamSettings
    {
        Environment=new EnvironmentSettings { Name="Demo" },
        Database=new DatabaseSettings { Provider="Sqlite",SqlitePath=Path.Combine(root,"t22.db"),CommandTimeoutSeconds=10 }
    };
    var db=new DemoSqliteDatabase(settings);
    await db.EnsureInitializedAsync();
    IAuditSink audit=new InMemoryAuditSink();

    var tapeStore=new TapeInventoryStore(null,db,audit);
    var config=new TapeManagementConfigurationStore(null,db,audit);
    var functions=new SystemFunctionStore(null,db,audit);

    var departments=await config.ListDepartmentsAsync(false);
    var media=departments.Single(x=>x.Code=="MEDIA");
    Equal("Media Department",media.NameEn,"Media Department English seed");
    Equal("الإدارة الإعلامية",media.NameAr,"Media Department Arabic seed");
    Require(media.IsActive,"Media Department seed is active");

    var archive=await config.UpsertDepartmentAsync(new UpsertTapeDepartmentRequest(
        "ARCHIVE","Archive Department","إدارة الأرشيف",true,20),"acceptance-admin");
    Equal("ARCHIVE",archive.Code,"authorized department upsert");
    Require(await config.IsActiveDepartmentAsync("ARCHIVE"),"new department is selectable");

    var tape=await tapeStore.CreateAsync(new CreateTapeRequest(
        "LEG-2026-01","نشرة الأخبار 2026","Official T2.2 barcode/report acceptance","HDCAM","Good","MEDIA",
        3600,new DateOnly(2026,1,15),"Media Room","CAB-01","S-02","B-03","Priority archive tape"),"acceptance-tape-operator");

    var descriptor=TapeBarcodePayload.For(tape);
    Require(descriptor.Payload.Contains(tape.TapeCode,StringComparison.Ordinal),"barcode payload contains durable tape code");
    Require(descriptor.Payload.Contains("TITLE=",StringComparison.Ordinal),"barcode payload contains encoded tape name");
    Equal(tape.TapeCode,TapeBarcodePayload.ExtractTapeCode(descriptor.Payload),"barcode payload resolves durable tape identity");

    var scanned=await tapeStore.ResolveCodeAsync(descriptor.Payload);
    Require(scanned is not null&&scanned.TapeId==tape.TapeId,"full barcode payload resolves authoritative tape");

    var byStatus=await tapeStore.ListAsync("NotDigitized",50);
    Require(byStatus.Items.Any(x=>x.TapeId==tape.TapeId),"text search includes digitization status");
    var byNotes=await tapeStore.ListAsync("Priority archive",50);
    Require(byNotes.Items.Any(x=>x.TapeId==tape.TapeId),"text search includes tape notes");
    var byFormat=await tapeStore.ListAsync("HDCAM",50);
    Require(byFormat.Items.Any(x=>x.TapeId==tape.TapeId),"text search includes tape format");

    await config.RecordPrintEventAsync(tape,new TapePrintEventRequest("label","Tape",60,30),"acceptance-printer");
    await config.RecordPrintEventAsync(tape,new TapePrintEventRequest("report",null,210,297),"acceptance-printer");
    var auditRows=await audit.ListRecentAsync(100);
    Require(auditRows.Any(x=>x.Action=="tape.barcode.printed"&&x.EntityId==tape.TapeCode),"barcode printing is audited");
    Require(auditRows.Any(x=>x.Action=="tape.report.printed"&&x.EntityId==tape.TapeCode),"tape report printing is audited");

    var systemRows=await functions.ListAsync();
    Require(systemRows.Any(x=>x.FunctionKey==MamSystemFunctionKeys.TapeSearchInContent&&x.IsEnabled),"tape content search feature is seeded enabled");
    var tapeSearch=systemRows.Single(x=>x.FunctionKey==MamSystemFunctionKeys.TapeSearchInContent);
    var disabled=await functions.UpdateAsync(tapeSearch.FunctionKey,new UpdateSystemFunctionRequest(false,tapeSearch.Version),"acceptance-admin");
    Require(!disabled.IsEnabled,"system function can be disabled authoritatively");
    Require(!await functions.IsEnabledAsync(MamSystemFunctionKeys.TapeSearchInContent),"disabled feature is enforced by service");
    var enabled=await functions.UpdateAsync(disabled.FunctionKey,new UpdateSystemFunctionRequest(true,disabled.Version),"acceptance-admin");
    Require(enabled.IsEnabled,"system function can be re-enabled authoritatively");

    Require(MamSecurity.PermissionsForRole(MamRoles.TapeManager).Contains(MamPermissions.TapeManageDepartments),"TapeManager can manage departments");
    Require(MamSecurity.PermissionsForRole(MamRoles.TapeManager).Contains(MamPermissions.TapeDelete),"TapeManager has independent delete permission");
    Require(MamSecurity.PermissionsForRole(MamRoles.TapeOperator).Contains(MamPermissions.TapePrint),"TapeOperator can print tape labels/reports");
    Require(!MamSecurity.PermissionsForRole(MamRoles.TapeOperator).Contains(MamPermissions.TapeDelete),"TapeOperator cannot delete tapes");
    Require(MamSecurity.PermissionsForRole(MamRoles.TapeViewer).Contains(MamPermissions.TapeSearch),"TapeViewer can search tapes");
    Require(!MamSecurity.PermissionsForRole(MamRoles.TapeViewer).Contains(MamPermissions.TapeEdit),"TapeViewer cannot edit tapes");

    CheckFile("src/MAM.Web/wwwroot/t22-tape-management.js",
        "50x25","60x30","70x40","100x50","custom","Manage departments","tape.print","print-events","resolve/");
    CheckFile("src/MAM.Web/wwwroot/t22-barcode.js",
        "211214","2331112","MAM|","TITLE=");
    CheckFile("src/MAM.Web/wwwroot/t22-tape-report.js",
        "Official Tape Report","report-grid","print-events","t22-barcode");
    CheckFile("src/MAM.Web/wwwroot/t22-full-content-report.js",
        "Full Content Report","byMediaType","totalFiles");
    CheckFile("src/MAM.Web/wwwroot/p09-operations.js",
        "p09FullContentReport","full-content-report.html","Official printable reports");
    CheckFile("src/MAM.Web/wwwroot/p135-unified-experience.js",
        "tape.search.in-content","mamUnifiedSource","tapes/content-search","data-mam-tape-open");
    CheckFile("src/MAM.Web/wwwroot/t22-system-routes.js",
        "system-functions.manage","tape.management","System Functions","Tape Management");
    CheckFile("src/MAM.Web/wwwroot/p126-experience-v2.js",
        "activeAssetIds","upload.primary.committed","Unknown user","lifecycle");
    CheckFile("src/MAM.Desktop/MainWindow.T21.cs",
        "T21DepartmentCombo","T21ResolveScanAsync","Print barcode","Tape report");
    CheckFile("src/MAM.Api/T2TapeInventoryEndpoints.cs",
        "TapeViewPolicy","TapeDeletePolicy","TapeManageDepartmentsPolicy","TapePrinting","content-search");
    CheckFile("src/MAM.Api/T22ReportingEndpoints.cs",
        "Full Content Report","Deleted","byMediaType","reports.printable");
    CheckFile("database/migrations/0017_t22_tape_labels_departments_system_functions.sql",
        "الإدارة الإعلامية","MamSystemFunction","TapeManager","TapeOperator","TapeViewer");

    Console.WriteLine("T2.2 TAPE LABELS / REPORTS / SYSTEM CONTROLS ACCEPTANCE: PASS");
    return 0;
}
finally
{
    try{Directory.Delete(root,true);}catch{}
}

static void CheckFile(string path,params string[] markers)
{
    var text=File.ReadAllText(path);
    foreach(var marker in markers)Require(text.Contains(marker,StringComparison.Ordinal),$"{path} marker: {marker}");
}

static void Require(bool condition,string name)
{
    if(!condition)throw new InvalidOperationException("FAILED: "+name);
    Console.WriteLine("PASS: "+name);
}

static void Equal<T>(T expected,T actual,string name)
{
    if(!EqualityComparer<T>.Default.Equals(expected,actual))
        throw new InvalidOperationException($"FAILED: {name}. Expected={expected}; Actual={actual}");
    Console.WriteLine("PASS: "+name);
}
