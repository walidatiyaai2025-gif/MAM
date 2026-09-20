using MAM.Application.Auditing;
using MAM.Application.SystemFunctions;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.SystemFunctions;

public sealed class SystemFunctionStore : ISystemFunctionService
{
    private readonly SqlServerConnectionFactory? _sql;
    private readonly DemoSqliteDatabase? _demo;
    private readonly IAuditSink _audit;

    public SystemFunctionStore(SqlServerConnectionFactory? sql, DemoSqliteDatabase? demo, IAuditSink audit)
    {
        _sql = sql;
        _demo = demo;
        _audit = audit;
        if ((_sql is null) == (_demo is null))
            throw new InvalidOperationException("System functions require exactly one SQL Server or Demo SQLite authority.");
    }

    public async Task<IReadOnlyList<SystemFunctionItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_sql is not null)
        {
            await using var c = await _sql.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand("""
                SELECT FunctionKey,NameEn,NameAr,IsEnabled,Version,UpdatedAtUtc,UpdatedBy
                FROM dbo.MamSystemFunction ORDER BY FunctionKey;
                """, c) { CommandTimeout = _sql.CommandTimeoutSeconds };
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            var rows = new List<SystemFunctionItem>();
            while (await r.ReadAsync(cancellationToken))
                rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.GetInt64(4),Utc(r.GetDateTime(5)),r.GetString(6)));
            return rows;
        }

        await using var dc = await OpenDemoAsync(cancellationToken);
        await using var dq = dc.CreateCommand();
        dq.CommandText = "SELECT FunctionKey,NameEn,NameAr,IsEnabled,Version,UpdatedAtUtc,UpdatedBy FROM DemoSystemFunction ORDER BY FunctionKey;";
        await using var dr = await dq.ExecuteReaderAsync(cancellationToken);
        var demoRows = new List<SystemFunctionItem>();
        while (await dr.ReadAsync(cancellationToken))
            demoRows.Add(new(dr.GetString(0),dr.GetString(1),dr.GetString(2),dr.GetInt32(3)!=0,dr.GetInt64(4),DemoSqliteDatabase.FromDb(dr.GetString(5)),dr.GetString(6)));
        return demoRows;
    }

    public async Task<bool> IsEnabledAsync(string functionKey, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(functionKey);
        var item = (await ListAsync(cancellationToken)).FirstOrDefault(x => string.Equals(x.FunctionKey,key,StringComparison.OrdinalIgnoreCase));
        return item?.IsEnabled == true;
    }

    public async Task<SystemFunctionItem> UpdateAsync(string functionKey, UpdateSystemFunctionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(functionKey);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedVersion <= 0)
            throw new SystemFunctionRequestException("expected_version_required","ExpectedVersion must be greater than zero.");

        var actor = NormalizeActor(actorId);
        if (_sql is not null)
        {
            await using var c = await _sql.OpenAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await using var cmd = new SqlCommand("""
                UPDATE dbo.MamSystemFunction
                SET IsEnabled=@Enabled,Version=Version+1,UpdatedAtUtc=@Now,UpdatedBy=@Actor
                WHERE FunctionKey=@Key AND Version=@ExpectedVersion;
                """, c) { CommandTimeout = _sql.CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@Enabled", request.IsEnabled);
            cmd.Parameters.AddWithValue("@Now", now.UtcDateTime);
            cmd.Parameters.AddWithValue("@Actor", actor);
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                var current = (await ListAsync(cancellationToken)).FirstOrDefault(x => string.Equals(x.FunctionKey,key,StringComparison.OrdinalIgnoreCase));
                if (current is null) throw new SystemFunctionRequestException("system_function_not_found","System function was not found.",404);
                throw new SystemFunctionRequestException("system_function_version_conflict","System function changed. Reload and retry.",409,current);
            }
        }
        else
        {
            await using var c = await OpenDemoAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await using var cmd = c.CreateCommand();
            cmd.CommandText = """
                UPDATE DemoSystemFunction SET IsEnabled=$enabled,Version=Version+1,UpdatedAtUtc=$now,UpdatedBy=$actor
                WHERE FunctionKey=$key AND Version=$version;
                """;
            cmd.Parameters.AddWithValue("$enabled", request.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(now));
            cmd.Parameters.AddWithValue("$actor", actor);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$version", request.ExpectedVersion);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                var current = (await ListAsync(cancellationToken)).FirstOrDefault(x => string.Equals(x.FunctionKey,key,StringComparison.OrdinalIgnoreCase));
                if (current is null) throw new SystemFunctionRequestException("system_function_not_found","System function was not found.",404);
                throw new SystemFunctionRequestException("system_function_version_conflict","System function changed. Reload and retry.",409,current);
            }
        }

        var saved = (await ListAsync(cancellationToken)).First(x => string.Equals(x.FunctionKey,key,StringComparison.OrdinalIgnoreCase));
        await _audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,"system-function.updated","SystemFunction",key,"Success",$"enabled={saved.IsEnabled};version={saved.Version}"),cancellationToken);
        return saved;
    }

    private static string NormalizeKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (!MamSystemFunctionKeys.All.Contains(key))
            throw new SystemFunctionRequestException("system_function_invalid","Unknown system function.",400);
        return key;
    }

    private static string NormalizeActor(string actor)
    {
        var value = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();
        return value[..Math.Min(value.Length,256)];
    }

    private async ValueTask<SqliteConnection> OpenDemoAsync(CancellationToken cancellationToken)
    {
        var c = await _demo!.OpenAsync(cancellationToken);
        await using var q = c.CreateCommand();
        q.CommandText = DemoSchema;
        await q.ExecuteNonQueryAsync(cancellationToken);
        return c;
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value,DateTimeKind.Utc));

    private const string DemoSchema = """
        CREATE TABLE IF NOT EXISTS DemoSystemFunction(
            FunctionKey TEXT PRIMARY KEY,
            NameEn TEXT NOT NULL,
            NameAr TEXT NOT NULL,
            IsEnabled INTEGER NOT NULL,
            Version INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            UpdatedBy TEXT NOT NULL);
        INSERT OR IGNORE INTO DemoSystemFunction VALUES('tape.management','Tape Management','إدارة الأشرطة',1,1,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoSystemFunction VALUES('tape.search.in-content','Tapes in Content Search','الأشرطة في البحث في المحتوى',1,1,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoSystemFunction VALUES('tape.printing','Tape Barcode and Report Printing','طباعة باركود وتقارير الأشرطة',1,1,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoSystemFunction VALUES('reports.printable','Printable Official Reports','التقارير الرسمية القابلة للطباعة',1,1,'2000-01-01T00:00:00.0000000Z','seed');
        """;
}
