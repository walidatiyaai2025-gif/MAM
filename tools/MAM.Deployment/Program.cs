using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

if (args.Length == 0) { Usage(); return 2; }
try
{
    return args[0].ToLowerInvariant() switch
    {
        "create-database" => await CreateDatabaseAsync(args),
        "migrate" => await MigrateAsync(args),
        "ensure-database-env" => await EnsureDatabaseFromEnvironmentAsync(args),
        "signature" => await SignatureAsync(args),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
    };
}
catch (Exception ex) { Console.Error.WriteLine($"MAM deployment command failed: {ex.Message}"); return 1; }

static async Task<int> CreateDatabaseAsync(string[] args)
{
    if (args.Length != 3) throw new ArgumentException("create-database requires: <master-connection-string> <database-name>");
    await EnsureDatabaseAsync(args[1], args[2]);
    return 0;
}

static async Task EnsureDatabaseAsync(string masterConnectionString, string databaseName)
{
    if (!Regex.IsMatch(databaseName, "^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant))
        throw new ArgumentException("database name contains unsupported characters");
    await using var connection = new SqlConnection(masterConnectionString);
    await connection.OpenAsync();
    await using var exists = new SqlCommand("SELECT DB_ID(@name)", connection);
    exists.Parameters.AddWithValue("@name", databaseName);
    if (await exists.ExecuteScalarAsync() is not DBNull and not null) { Console.WriteLine("{\"status\":\"exists\"}"); return; }
    await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection) { CommandTimeout = 60 };
    await create.ExecuteNonQueryAsync();
    Console.WriteLine("{\"status\":\"created\"}");
}

static async Task<int> MigrateAsync(string[] args)
{
    if (args.Length != 3) throw new ArgumentException("migrate requires: <connection-string> <migration-directory>");
    await MigrateConnectionAsync(args[1], args[2]);
    return 0;
}

static async Task MigrateConnectionAsync(string connectionString, string migrationDirectory)
{
    var factory = new SqlServerConnectionFactory(connectionString, 60, enableRetryOnFailure: true);
    var runner = new SqlServerMigrationRunner(factory);
    var applied = await runner.ApplyDirectoryAsync(migrationDirectory);
    Console.WriteLine(JsonSerializer.Serialize(new { status = "ok", applied }));
}

static async Task<int> EnsureDatabaseFromEnvironmentAsync(string[] args)
{
    if (args.Length != 3) throw new ArgumentException("ensure-database-env requires: <environment-variable-name> <migration-directory>");
    var value = Environment.GetEnvironmentVariable(args[1]);
    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Environment variable '{args[1]}' is not set.");

    var target = new SqlConnectionStringBuilder(value);
    var databaseName = target.InitialCatalog?.Trim();
    if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("SQL connection string must include Initial Catalog/Database.");

    var master = new SqlConnectionStringBuilder(value) { InitialCatalog = "master" };
    await EnsureDatabaseAsync(master.ConnectionString, databaseName);
    await MigrateConnectionAsync(target.ConnectionString, args[2]);
    return 0;
}

static async Task<int> SignatureAsync(string[] args)
{
    if (args.Length != 2) throw new ArgumentException("signature requires: <connection-string>");
    await using var connection = new SqlConnection(args[1]);
    await connection.OpenAsync();
    var tables = new[]
    {
        (Name: "MamSchemaVersion", Sql: "SELECT MigrationId FROM dbo.MamSchemaVersion ORDER BY MigrationId"),
        (Name: "MediaAsset", Sql: "SELECT CONVERT(nvarchar(36),AssetId),Title,CONVERT(nvarchar(20),Lifecycle),CONVERT(nvarchar(30),Version) FROM dbo.MediaAsset ORDER BY AssetId"),
        (Name: "MamMediaOriginal", Sql: "SELECT CONVERT(nvarchar(36),AssetId),StorageTargetId,ObjectKey,OriginalFileName,CONVERT(nvarchar(30),Length),Sha256 FROM dbo.MamMediaOriginal ORDER BY AssetId")
    };
    var output = new SortedDictionary<string, object>(StringComparer.Ordinal);
    foreach (var table in tables)
    {
        await using var exists = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@name,N'U') IS NULL THEN 0 ELSE 1 END", connection);
        exists.Parameters.AddWithValue("@name", "dbo." + table.Name);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) { output[table.Name] = new { count = 0, sha256 = Sha256("") }; continue; }
        await using var command = new SqlCommand(table.Sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            rows.Add(string.Join("\u001f", values));
        }
        output[table.Name] = new { count = rows.Count, sha256 = Sha256(string.Join("\n", rows)) };
    }
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
static void Usage() => Console.Error.WriteLine("Usage: MAM.Deployment create-database <master-connection-string> <database-name> | migrate <connection-string> <migration-directory> | ensure-database-env <environment-variable-name> <migration-directory> | signature <connection-string>");
