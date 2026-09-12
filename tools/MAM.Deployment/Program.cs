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
        "signature" => await SignatureAsync(args),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
    };
}
catch (Exception ex) { Console.Error.WriteLine($"MAM deployment command failed: {ex.Message}"); return 1; }

static async Task<int> CreateDatabaseAsync(string[] args)
{
    if (args.Length != 3) throw new ArgumentException("create-database requires: <master-connection-string> <database-name>");
    if (!Regex.IsMatch(args[2], "^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant)) throw new ArgumentException("database name contains unsupported characters");
    await using var connection = new SqlConnection(args[1]);
    await connection.OpenAsync();
    await using var exists = new SqlCommand("SELECT DB_ID(@name)", connection);
    exists.Parameters.AddWithValue("@name", args[2]);
    if (await exists.ExecuteScalarAsync() is not DBNull and not null) { Console.WriteLine("{\"status\":\"exists\"}"); return 0; }
    await using var create = new SqlCommand($"CREATE DATABASE [{args[2]}]", connection) { CommandTimeout = 60 };
    await create.ExecuteNonQueryAsync();
    Console.WriteLine("{\"status\":\"created\"}");
    return 0;
}

static async Task<int> MigrateAsync(string[] args)
{
    if (args.Length != 3) throw new ArgumentException("migrate requires: <connection-string> <migration-directory>");
    var factory = new SqlServerConnectionFactory(args[1], 60, enableRetryOnFailure: true);
    var runner = new SqlServerMigrationRunner(factory);
    var applied = await runner.ApplyDirectoryAsync(args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new { status = "ok", applied }));
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
static void Usage() => Console.Error.WriteLine("Usage: MAM.Deployment create-database <master-connection-string> <database-name> | migrate <connection-string> <migration-directory> | signature <connection-string>");
