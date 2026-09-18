using MAM.Application.Auditing;
using MAM.Application.Tapes;
using MAM.Domain.Tapes;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Configuration;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Tapes;
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
    var firstMigrationPass = await migrations.ApplyDirectoryAsync(migrationDirectory);
    var secondMigrationPass = await migrations.ApplyDirectoryAsync(migrationDirectory);

    Require(firstMigrationPass.Contains("0014_t2_tape_inventory.sql", StringComparer.Ordinal),
        "SQL migration 0014 is applied");
    Equal(firstMigrationPass.Count, secondMigrationPass.Count,
        "SQL migrations are repeatable without duplicate schema changes");

    IAuditSink audit = new SqlServerAuditSink(connections);
    var store = new TapeInventoryStore(connections, null, audit);
    var first = await RunInventorySuiteAsync(store, audit, "SQL Server", exerciseConcurrentAllocation: true);

    var reopened = new TapeInventoryStore(connections, null, audit);
    var persisted = await reopened.ResolveCodeAsync(first.TapeCode);
    Require(persisted is not null && persisted.TapeId == first.TapeId && persisted.Version == 2,
        "SQL tape record persists across service reconstruction");

    await using (var connection = await connections.OpenAsync())
    await using (var command = new SqlCommand(
        "SELECT COUNT(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0014_t2_tape_inventory';", connection))
    {
        Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()),
            "SQL schema version records migration 0014 exactly once");
    }

    Console.WriteLine("T2.1 SQL SERVER TAPE INVENTORY ACCEPTANCE: PASS");
    return 0;
}

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
    IAuditSink audit = new InMemoryAuditSink();
    var store = new TapeInventoryStore(null, db, audit);
    var first = await RunInventorySuiteAsync(store, audit, "Demo SQLite", exerciseConcurrentAllocation: false);

    var reopenedDb = new DemoSqliteDatabase(settings);
    await reopenedDb.EnsureInitializedAsync();
    var reopened = new TapeInventoryStore(null, reopenedDb, audit);
    var persisted = await reopened.ResolveCodeAsync(first.TapeCode);
    Require(persisted is not null && persisted.TapeId == first.TapeId && persisted.Version == 2,
        "Demo SQLite tape record persists across service reconstruction");

    Console.WriteLine("T2.1 DEMO SQLITE TAPE INVENTORY ACCEPTANCE: PASS");
    return 0;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static async Task<TapeInventoryItem> RunInventorySuiteAsync(
    ITapeInventoryService store,
    IAuditSink audit,
    string provider,
    bool exerciseConcurrentAllocation)
{
    var formats = await store.ListFormatsAsync(false);
    Require(formats.Any(x => x.Code == "HDCAM"), $"{provider}: HDCAM seeded format is available");
    Require(formats.Any(x => x.Code == "BETACAM"), $"{provider}: Betacam seeded format is available");

    var custom = await store.UpsertFormatAsync(
        new UpsertTapeFormatRequest("UMATIC", "U-matic", "يو-ماتيك", true, 50),
        "acceptance-admin");
    Equal("UMATIC", custom.Code, $"{provider}: administrators can configure tape formats");

    var first = await store.CreateAsync(new CreateTapeRequest(
        "OLD-77", "Official archive tape", "Source finding aid", "HDCAM", "Good", "Archive",
        3600, new DateOnly(2005, 5, 17), "R1", "C2", "S3", "B4", "T2.1 acceptance"), "acceptance-editor");
    var second = await store.CreateAsync(new CreateTapeRequest(
        null, null, null, "BETACAM", null, null, null, null, null, null, null, null, null), "acceptance-editor");

    Equal("TAPE-000001", first.TapeCode, $"{provider}: first durable tape code");
    Equal("TAPE-000002", second.TapeCode, $"{provider}: second durable tape code");
    Equal(TapeDigitizationStatuses.NotDigitized, first.DigitizationStatus,
        $"{provider}: create status defaults to NotDigitized");
    Require(second.Title is null && second.PhysicalCondition is null,
        $"{provider}: unknown optional values stay null/unknown");

    if (exerciseConcurrentAllocation)
    {
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            store.CreateAsync(new CreateTapeRequest(
                $"CONCURRENT-{i:D2}", null, null, "HDCAM", null, null,
                null, null, null, null, null, null, null), $"concurrent-editor-{i:D2}")));

        Require(concurrent.Select(x => x.TapeCode).Distinct(StringComparer.Ordinal).Count() == concurrent.Length,
            $"{provider}: concurrent tape allocation remains unique");
        Require(concurrent.All(x =>
                x.TapeCode.StartsWith(TapeCode.Prefix, StringComparison.Ordinal) &&
                x.TapeCode.Length == TapeCode.Prefix.Length + TapeCode.SequenceDigits &&
                x.TapeCode.AsSpan(TapeCode.Prefix.Length).ToString().All(char.IsDigit)),
            $"{provider}: concurrent allocations preserve TAPE-###### identity format");
    }

    var updated = await store.UpdateAsync(first.TapeId, new UpdateTapeRequest(
        first.LegacyNumber, "Official archive tape — reviewed", first.Description, first.TapeFormatCode, "Fair",
        TapeDigitizationStatuses.SentForDigitization, first.OwnerDepartment, first.DurationSeconds, first.RecordingDate,
        first.Room, first.Cabinet, first.Shelf, first.Bin, "Sent to external digitization team", first.Version), "acceptance-editor");

    Equal(first.TapeCode, updated.TapeCode, $"{provider}: tape code remains immutable after edit");
    Equal(2, updated.Version, $"{provider}: optimistic version increments");
    Equal(TapeDigitizationStatuses.SentForDigitization, updated.DigitizationStatus,
        $"{provider}: digitization state is independently updateable");
    Equal("Fair", updated.PhysicalCondition,
        $"{provider}: physical condition is independently updateable");

    var byCode = await store.ResolveCodeAsync("tape-000001");
    Require(byCode is not null && byCode.TapeId == first.TapeId,
        $"{provider}: code resolve is normalized and stable");

    var search = await store.ListAsync("OLD-77", 50);
    Require(search.Total == 1 && search.Items.Single().TapeId == first.TapeId,
        $"{provider}: legacy-number search returns authoritative tape");

    var conflictObserved = false;
    try
    {
        await store.UpdateAsync(first.TapeId, new UpdateTapeRequest(
            first.LegacyNumber, "stale edit", first.Description, first.TapeFormatCode, first.PhysicalCondition,
            TapeDigitizationStatuses.NotDigitized, first.OwnerDepartment, first.DurationSeconds, first.RecordingDate,
            first.Room, first.Cabinet, first.Shelf, first.Bin, first.Notes, first.Version), "stale-editor");
    }
    catch (TapeInventoryRequestException ex) when (
        ex.Code == "tape_version_conflict" &&
        ex.StatusCode == 409 &&
        ex.Current?.Version == 2)
    {
        conflictObserved = true;
    }
    Require(conflictObserved, $"{provider}: stale edits fail closed with current authoritative version");

    var invalidStatusObserved = false;
    try
    {
        await store.UpdateAsync(second.TapeId, new UpdateTapeRequest(
            null, null, null, "BETACAM", null, "CapturedInsideMam", null, null, null,
            null, null, null, null, null, second.Version), "acceptance-editor");
    }
    catch (TapeInventoryRequestException ex) when (ex.Code == "invalid_digitization_status")
    {
        invalidStatusObserved = true;
    }
    Require(invalidStatusObserved, $"{provider}: in-product capture state is rejected");

    var events = await audit.ListRecentAsync(200);
    Require(events.Any(x => x.Action == "tape.create" && x.EntityId == first.TapeCode),
        $"{provider}: create is audited");
    Require(events.Any(x => x.Action == "tape.update" && x.EntityId == first.TapeCode),
        $"{provider}: update is audited");

    return updated;
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
