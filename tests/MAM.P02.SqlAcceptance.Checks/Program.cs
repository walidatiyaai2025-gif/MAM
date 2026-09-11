using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Application.Metadata;
using MAM.Domain.Assets;
using MAM.Infrastructure.Auditing;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: MAM.P02.SqlAcceptance.Checks <master-connection-string> <database-name> <migration-directory>");
    return 2;
}

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
var first = await migrations.ApplyDirectoryAsync(migrationDirectory);
var second = await migrations.ApplyDirectoryAsync(migrationDirectory);
if (first.Count < 2 || second.Count != first.Count)
    throw new InvalidOperationException($"Expected repeatable migration execution. first={first.Count}, second={second.Count}");

await AssertSchemaAsync(connections);

IAuditSink audit = new SqlServerAuditSink(connections);
IAssetCatalog catalog = new SqlServerAssetCatalog(connections, audit);
var health = await catalog.GetHealthAsync();
if (!health.IsReady || !string.Equals(health.Provider, "SqlServer", StringComparison.Ordinal))
    throw new InvalidOperationException($"SQL catalog health failed: {health.Provider} / {health.Detail}");

var created = await catalog.CreateAsync("P02 SQL Acceptance Asset", "desktop-acceptance");
if (created.Status != CatalogMutationStatus.Created || created.Asset is null || created.Asset.Version != 1)
    throw new InvalidOperationException("SQL catalog create did not return a version-1 asset.");

var fromSecondClient = (await catalog.ListAsync()).SingleOrDefault(asset => asset.Id == created.Asset.Id);
if (fromSecondClient is null || fromSecondClient.Title != "P02 SQL Acceptance Asset")
    throw new InvalidOperationException("SQL catalog shared state was not observable from a separate catalog read.");

var stale = await catalog.UpdateTitleAsync(new AssetId(created.Asset.Id), "Stale update", 0, "web-acceptance");
if (stale.Status != CatalogMutationStatus.Conflict || stale.Asset?.Version != 1)
    throw new InvalidOperationException("SQL optimistic concurrency did not reject a stale version.");

var updated = await catalog.UpdateTitleAsync(new AssetId(created.Asset.Id), "P02 SQL Renamed Asset", 1, "web-acceptance");
if (updated.Status != CatalogMutationStatus.Updated || updated.Asset?.Version != 2)
    throw new InvalidOperationException("SQL optimistic concurrency did not advance the current version.");

var finalRead = await catalog.GetAsync(new AssetId(created.Asset.Id));
if (finalRead?.Title != "P02 SQL Renamed Asset" || finalRead.Version != 2)
    throw new InvalidOperationException("SQL catalog did not persist the successful metadata edit.");

var events = await audit.ListRecentAsync(20);
var actions = events.Select(item => item.Action).ToHashSet(StringComparer.Ordinal);
if (!actions.Contains("catalog.asset.created") || !actions.Contains("catalog.asset.title-updated") || !actions.Contains("catalog.asset.title-update-conflict"))
    throw new InvalidOperationException("SQL audit evidence is missing create/update/conflict events.");

var registry = new BuiltInMetadataSchemaRegistry();
var schema = registry.Get(BuiltInMetadataSchemaRegistry.CoreMediaSchemaKey);
if (schema is null || schema.Fields.Count < 5 || !schema.Fields.Any(field => field.Key == "title" && field.Required))
    throw new InvalidOperationException("Built-in metadata schema baseline is incomplete.");
var valid = registry.Validate(schema.Key, new Dictionary<string, string?> { ["title"] = "Validated title", ["eventDate"] = "2026-09-11" });
if (valid.Count != 0) throw new InvalidOperationException("Valid metadata payload was rejected.");
var invalid = registry.Validate(schema.Key, new Dictionary<string, string?> { ["title"] = "" });
if (!invalid.Any(error => error.FieldKey == "title")) throw new InvalidOperationException("Required metadata field validation did not fail closed.");

Console.WriteLine($"PASS: clean SQL database initialized with {first.Count} migrations; repeat execution remained safe.");
Console.WriteLine("PASS: SQL-backed create/read/update conflict/audit/readiness and metadata schema validation succeeded.");
return 0;

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
    var sql = $"""
        IF DB_ID(N'{databaseName.Replace("'", "''", StringComparison.Ordinal)}') IS NOT NULL
        BEGIN
            ALTER DATABASE [{escaped}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{escaped}];
        END;
        CREATE DATABASE [{escaped}];
        """;
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
    await command.ExecuteNonQueryAsync();
}

static async Task AssertSchemaAsync(SqlServerConnectionFactory connections)
{
    await using var connection = await connections.OpenAsync();
    const string sql = """
        SELECT
            (SELECT COUNT(*) FROM dbo.MamSchemaVersion) AS MigrationCount,
            (SELECT COUNT(*) FROM dbo.MamMetadataSchema WHERE SchemaKey=N'core-media-v1') AS MetadataSchemaCount,
            (SELECT COUNT(*) FROM dbo.MamMetadataField WHERE SchemaKey=N'core-media-v1') AS MetadataFieldCount;
        """;
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("SQL schema verification returned no row.");
    if (reader.GetInt32(0) < 2 || reader.GetInt32(1) != 1 || reader.GetInt32(2) < 5)
        throw new InvalidOperationException($"SQL schema verification failed: migrations={reader.GetInt32(0)}, schemas={reader.GetInt32(1)}, fields={reader.GetInt32(2)}");
}
