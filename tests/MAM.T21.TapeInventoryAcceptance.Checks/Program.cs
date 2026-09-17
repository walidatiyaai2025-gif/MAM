using MAM.Application.Tapes;
using MAM.Domain.Tapes;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Tapes;

var root = Path.Combine(Path.GetTempPath(), "mam-t21-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var settings = new MamSettings
    {
        Environment = new EnvironmentSettings { Name = "Demo" },
        Database = new DatabaseSettings
        {
            Provider = "Sqlite",
            SqlitePath = Path.Combine(root, "t21.db"),
            CommandTimeoutSeconds = 10
        }
    };

    var db = new DemoSqliteDatabase(settings);
    await db.EnsureInitializedAsync();
    var audit = new InMemoryAuditSink();
    var store = new TapeInventoryStore(null, db, audit);

    var formats = await store.ListFormatsAsync(false);
    Require(formats.Any(x => x.Code == "HDCAM"), "HDCAM seeded format is available");
    Require(formats.Any(x => x.Code == "BETACAM"), "Betacam seeded format is available");

    var first = await store.CreateAsync(new CreateTapeRequest(
        "OLD-77", "Official archive tape", "Source finding aid", "HDCAM", "Good", "Archive",
        3600, new DateOnly(2005, 5, 17), "R1", "C2", "S3", "B4", "T2.1 acceptance"), "acceptance-editor");
    var second = await store.CreateAsync(new CreateTapeRequest(
        null, null, null, "BETACAM", null, null, null, null, null, null, null, null, null), "acceptance-editor");

    Equal("TAPE-000001", first.TapeCode, "first durable tape code");
    Equal("TAPE-000002", second.TapeCode, "second durable tape code");
    Equal(TapeDigitizationStatuses.NotDigitized, first.DigitizationStatus, "create status defaults to NotDigitized");
    Require(second.Title is null && second.PhysicalCondition is null, "unknown optional values stay null/unknown");

    var updated = await store.UpdateAsync(first.TapeId, new UpdateTapeRequest(
        first.LegacyNumber, "Official archive tape — reviewed", first.Description, first.TapeFormatCode, "Fair",
        TapeDigitizationStatuses.SentForDigitization, first.OwnerDepartment, first.DurationSeconds, first.RecordingDate,
        first.Room, first.Cabinet, first.Shelf, first.Bin, "Sent to external digitization team", first.Version), "acceptance-editor");

    Equal(first.TapeCode, updated.TapeCode, "tape code remains immutable after edit");
    Equal(2, updated.Version, "optimistic version increments");
    Equal(TapeDigitizationStatuses.SentForDigitization, updated.DigitizationStatus, "digitization state is independent and updateable");
    Equal("Fair", updated.PhysicalCondition, "physical condition is independently updateable");

    var byCode = await store.ResolveCodeAsync("tape-000001");
    Require(byCode is not null && byCode.TapeId == first.TapeId, "code resolve is normalized and stable");

    var search = await store.ListAsync("OLD-77", 50);
    Require(search.Total == 1 && search.Items.Single().TapeId == first.TapeId, "legacy-number search returns authoritative tape");

    var conflictObserved = false;
    try
    {
        await store.UpdateAsync(first.TapeId, new UpdateTapeRequest(
            first.LegacyNumber, "stale edit", first.Description, first.TapeFormatCode, first.PhysicalCondition,
            TapeDigitizationStatuses.NotDigitized, first.OwnerDepartment, first.DurationSeconds, first.RecordingDate,
            first.Room, first.Cabinet, first.Shelf, first.Bin, first.Notes, first.Version), "stale-editor");
    }
    catch (TapeInventoryRequestException ex) when (ex.Code == "tape_version_conflict" && ex.StatusCode == 409 && ex.Current?.Version == 2)
    {
        conflictObserved = true;
    }
    Require(conflictObserved, "stale edits fail closed with current authoritative version");

    var invalidStatusObserved = false;
    try
    {
        await store.UpdateAsync(second.TapeId, new UpdateTapeRequest(
            null, null, null, "BETACAM", null, "CapturedInsideMam", null, null, null, null, null, null, null, null, second.Version), "acceptance-editor");
    }
    catch (TapeInventoryRequestException ex) when (ex.Code == "invalid_digitization_status")
    {
        invalidStatusObserved = true;
    }
    Require(invalidStatusObserved, "invalid/in-product capture state is rejected");

    var events = await audit.ListRecentAsync(20);
    Require(events.Any(x => x.Action == "tape.create" && x.EntityId == first.TapeCode), "create is audited");
    Require(events.Any(x => x.Action == "tape.update" && x.EntityId == first.TapeCode), "update is audited");

    Console.WriteLine("T2.1 TAPE INVENTORY ACCEPTANCE: PASS");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
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
