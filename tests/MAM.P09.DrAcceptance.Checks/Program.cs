using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MAM.P09.DrAcceptance.Checks <master-connection-string> [source-db] [restore-db]");
    return 2;
}

var sourceDatabase = args.Length > 1 ? args[1] : "MamP02Ci";
var restoreDatabase = args.Length > 2 ? args[2] : "MamP09Restore";
if (!SafeIdentifier(sourceDatabase) || !SafeIdentifier(restoreDatabase) || string.Equals(sourceDatabase, restoreDatabase, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Database names must be distinct simple SQL identifiers.");
    return 2;
}

var masterBuilder = new SqlConnectionStringBuilder(args[0]) { InitialCatalog = "master" };
var backupPath = "/var/opt/mssql/data/mam-p09-dr-acceptance.bak";
try
{
    await using var master = new SqlConnection(masterBuilder.ConnectionString);
    await master.OpenAsync();

    if (!await DatabaseExistsAsync(master, sourceDatabase))
        throw new InvalidOperationException("Source acceptance database does not exist.");

    var logical = await LogicalFilesAsync(master, sourceDatabase);
    if (logical.Data is null || logical.Log is null)
        throw new InvalidOperationException("Source database logical data/log files could not be resolved.");

    var sourceSignature = await ReadSignatureAsync(masterBuilder, sourceDatabase);
    await DropDatabaseIfExistsAsync(master, restoreDatabase);

    await ExecuteAsync(master, $"BACKUP DATABASE [{sourceDatabase}] TO DISK = N'{backupPath}' WITH INIT, COPY_ONLY, CHECKSUM;");
    await ExecuteAsync(master, $"RESTORE VERIFYONLY FROM DISK = N'{backupPath}' WITH CHECKSUM;");
    await ExecuteAsync(master, $"RESTORE DATABASE [{restoreDatabase}] FROM DISK = N'{backupPath}' WITH CHECKSUM, MOVE N'{SqlLiteral(logical.Data)}' TO N'/var/opt/mssql/data/{restoreDatabase}.mdf', MOVE N'{SqlLiteral(logical.Log)}' TO N'/var/opt/mssql/data/{restoreDatabase}_log.ldf';");

    var restoredSignature = await ReadSignatureAsync(masterBuilder, restoreDatabase);
    if (sourceSignature != restoredSignature)
        throw new InvalidOperationException($"Restored database signature differs from source. Source={sourceSignature}; Restored={restoredSignature}");

    Console.WriteLine($"P09 SQL backup/restore acceptance: PASS source={sourceDatabase} restore={restoreDatabase} assets={sourceSignature.Assets} originals={sourceSignature.Originals} bytes={sourceSignature.OriginalBytes} migrations={sourceSignature.Migrations}");
    await DropDatabaseIfExistsAsync(master, restoreDatabase);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"P09 SQL backup/restore acceptance: FAIL ({ex.GetType().Name}) {Sanitize(ex.Message)}");
    try
    {
        await using var cleanup = new SqlConnection(masterBuilder.ConnectionString);
        await cleanup.OpenAsync();
        await DropDatabaseIfExistsAsync(cleanup, restoreDatabase);
    }
    catch { }
    return 1;
}

static bool SafeIdentifier(string value) => Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
static string Sanitize(string value)
{
    value = Regex.Replace(value, "(?i)(password|pwd)=[^; ]+", "$1=***");
    return value.Length <= 1000 ? value : value[..1000];
}

static async Task<bool> DatabaseExistsAsync(SqlConnection master, string database)
{
    await using var command = new SqlCommand("SELECT CASE WHEN DB_ID(@database) IS NULL THEN 0 ELSE 1 END", master);
    command.Parameters.AddWithValue("@database", database);
    return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
}

static async Task<(string? Data, string? Log)> LogicalFilesAsync(SqlConnection master, string database)
{
    await using var command = new SqlCommand("SELECT name, CAST(type AS int) FROM sys.master_files WHERE database_id=DB_ID(@database) AND type IN (0,1) ORDER BY type", master);
    command.Parameters.AddWithValue("@database", database);
    await using var reader = await command.ExecuteReaderAsync();
    string? data = null, log = null;
    while (await reader.ReadAsync())
    {
        if (reader.GetInt32(1) == 0 && data is null) data = reader.GetString(0);
        if (reader.GetInt32(1) == 1 && log is null) log = reader.GetString(0);
    }
    return (data, log);
}

static async Task<DatabaseSignature> ReadSignatureAsync(SqlConnectionStringBuilder masterBuilder, string database)
{
    var builder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString) { InitialCatalog = database };
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    const string sql = """
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.MediaAsset),
 (SELECT COUNT_BIG(*) FROM dbo.MamMediaOriginal),
 (SELECT COALESCE(SUM([Length]),0) FROM dbo.MamMediaOriginal),
 (SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion),
 (SELECT COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(AssetId,Title,[Version])),0) FROM dbo.MediaAsset),
 (SELECT COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(AssetId,[Length],Sha256)),0) FROM dbo.MamMediaOriginal)
""";
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("Database signature query returned no row.");
    return new DatabaseSignature(reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt64(reader.GetValue(2)), reader.GetInt64(3), reader.GetInt32(4), reader.GetInt32(5));
}

static async Task DropDatabaseIfExistsAsync(SqlConnection master, string database)
{
    if (!await DatabaseExistsAsync(master, database)) return;
    await ExecuteAsync(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
}

static async Task ExecuteAsync(SqlConnection connection, string sql)
{
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
    await command.ExecuteNonQueryAsync();
}

internal sealed record DatabaseSignature(long Assets, long Originals, long OriginalBytes, long Migrations, int AssetChecksum, int OriginalChecksum);
