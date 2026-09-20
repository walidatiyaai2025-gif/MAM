using System.Data;
using MAM.Application.Auditing;
using MAM.Application.Tapes;
using MAM.Domain.Tapes;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Tapes;

public sealed class TapeInventoryStore : ITapeInventoryService
{
    private readonly SqlServerConnectionFactory? _sql;
    private readonly DemoSqliteDatabase? _demo;
    private readonly IAuditSink _audit;

    public TapeInventoryStore(SqlServerConnectionFactory? sql, DemoSqliteDatabase? demo, IAuditSink audit)
    {
        _sql = sql;
        _demo = demo;
        _audit = audit;
        if ((_sql is null) == (_demo is null))
            throw new InvalidOperationException("Tape inventory requires exactly one authoritative SQL Server or Demo SQLite store.");
    }

    public Task<TapeInventoryPage> ListAsync(string? query, int limit, CancellationToken cancellationToken = default) =>
        _sql is not null ? ListSqlAsync(query, limit, cancellationToken) : ListDemoAsync(query, limit, cancellationToken);

    public Task<TapeInventoryItem?> GetAsync(Guid tapeId, CancellationToken cancellationToken = default) =>
        _sql is not null ? GetSqlAsync(tapeId, cancellationToken) : GetDemoAsync(tapeId, cancellationToken);

    public Task<TapeInventoryItem?> ResolveCodeAsync(string tapeCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCode(tapeCode);
        return _sql is not null ? ResolveSqlAsync(normalized, cancellationToken) : ResolveDemoAsync(normalized, cancellationToken);
    }

    public Task<TapeInventoryItem> CreateAsync(CreateTapeRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var input = ValidateCreate(request);
        var actor = Actor(actorId);
        return _sql is not null ? CreateSqlAsync(input, actor, cancellationToken) : CreateDemoAsync(input, actor, cancellationToken);
    }

    public Task<TapeInventoryItem> UpdateAsync(Guid tapeId, UpdateTapeRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (tapeId == Guid.Empty) throw Bad("invalid_tape_id", "Tape id is required.");
        var input = ValidateUpdate(request);
        var actor = Actor(actorId);
        return _sql is not null ? UpdateSqlAsync(tapeId, input, actor, cancellationToken) : UpdateDemoAsync(tapeId, input, actor, cancellationToken);
    }

    public Task DeleteAsync(Guid tapeId, int expectedVersion, string actorId, CancellationToken cancellationToken = default)
    {
        if (tapeId == Guid.Empty) throw Bad("invalid_tape_id", "Tape id is required.");
        if (expectedVersion < 1) throw Bad("invalid_expected_version", "Expected version must be at least 1.");
        var actor = Actor(actorId);
        return _sql is not null ? DeleteSqlAsync(tapeId, expectedVersion, actor, cancellationToken) : DeleteDemoAsync(tapeId, expectedVersion, actor, cancellationToken);
    }

    public Task<IReadOnlyList<TapeFormatItem>> ListFormatsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _sql is not null ? ListFormatsSqlAsync(includeInactive, cancellationToken) : ListFormatsDemoAsync(includeInactive, cancellationToken);

    public Task<TapeFormatItem> UpsertFormatAsync(UpsertTapeFormatRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var code = NormalizeFormatCode(request.Code);
        var nameEn = Required(request.NameEn, 128, "name_en_required");
        var nameAr = Required(request.NameAr, 128, "name_ar_required");
        var normalized = request with { Code = code, NameEn = nameEn, NameAr = nameAr };
        var actor = Actor(actorId);
        return _sql is not null ? UpsertFormatSqlAsync(normalized, actor, cancellationToken) : UpsertFormatDemoAsync(normalized, actor, cancellationToken);
    }

    private async Task<TapeInventoryPage> ListSqlAsync(string? query, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var q = NormalizeOptional(query, 512);
        await using var c = await _sql!.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (@limit) TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,
                   OwnerDepartment,DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,
                   COUNT(*) OVER() AS TotalCount
            FROM dbo.inv_tapes
            WHERE @q IS NULL OR TapeCode LIKE @like OR LegacyNumber LIKE @like OR Title LIKE @like OR Description LIKE @like
               OR TapeFormatCode LIKE @like OR PhysicalCondition LIKE @like OR DigitizationStatus LIKE @like
               OR OwnerDepartment LIKE @like OR Room LIKE @like OR Cabinet LIKE @like OR Shelf LIKE @like OR Bin LIKE @like OR Notes LIKE @like
            ORDER BY UpdatedAtUtc DESC,TapeCode DESC;
            """, c) { CommandTimeout = _sql.CommandTimeoutSeconds };
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@q", (object?)q ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@like", q is null ? DBNull.Value : $"%{EscapeLike(q)}%");
        var rows = new List<TapeInventoryItem>();
        var total = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (rows.Count == 0) total = r.GetInt32(21);
            rows.Add(ReadSql(r));
        }
        return new(rows, total);
    }

    private async Task<TapeInventoryItem?> GetSqlAsync(Guid tapeId, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        return await GetSqlAsync(c, null, tapeId, null, ct);
    }

    private async Task<TapeInventoryItem?> ResolveSqlAsync(string code, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        return await GetSqlAsync(c, null, null, code, ct);
    }

    private async Task<TapeInventoryItem> CreateSqlAsync(CreateTapeRequest request, string actor, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await EnsureFormatSqlAsync(c, tx, request.TapeFormatCode, ct);
        long sequence;
        await using (var counter = new SqlCommand("""
            UPDATE dbo.inv_counters WITH (UPDLOCK,HOLDLOCK)
            SET LastValue=LastValue+1,UpdatedAtUtc=SYSUTCDATETIME()
            OUTPUT inserted.LastValue
            WHERE CounterKey=N'tape';
            """, c, tx))
        {
            sequence = Convert.ToInt64(await counter.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Tape counter was not initialized."));
        }
        var item = NewItem(request, TapeCode.FromSequence(sequence), actor);
        await using (var cmd = new SqlCommand("""
            INSERT dbo.inv_tapes(TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,
                OwnerDepartment,DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy)
            VALUES(@id,@code,@legacy,@title,@description,@format,@condition,@status,@owner,@duration,@recordingDate,@room,@cabinet,@shelf,@bin,@notes,
                1,@created,@actor,@created,@actor);
            """, c, tx))
        {
            AddTapeParameters(cmd, item);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.create", item, "Success", ct);
        return item;
    }

    private async Task<TapeInventoryItem> UpdateSqlAsync(Guid tapeId, UpdateTapeRequest request, string actor, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await GetSqlAsync(c, tx, tapeId, null, ct) ?? throw NotFound();
        if (current.Version != request.ExpectedVersion) throw Conflict(current);
        await EnsureFormatSqlAsync(c, tx, request.TapeFormatCode, ct);
        var updated = Apply(current, request, actor);
        await using var cmd = new SqlCommand("""
            UPDATE dbo.inv_tapes SET LegacyNumber=@legacy,Title=@title,Description=@description,TapeFormatCode=@format,
                PhysicalCondition=@condition,DigitizationStatus=@status,OwnerDepartment=@owner,DurationSeconds=@duration,
                RecordingDate=@recordingDate,Room=@room,Cabinet=@cabinet,Shelf=@shelf,Bin=@bin,Notes=@notes,
                Version=@version,UpdatedAtUtc=@updated,UpdatedBy=@actor
            WHERE TapeId=@id AND Version=@expected;
            """, c, tx);
        AddTapeParameters(cmd, updated);
        cmd.Parameters.AddWithValue("@expected", request.ExpectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
        {
            var latest = await GetSqlAsync(c, tx, tapeId, null, ct);
            throw Conflict(latest ?? current);
        }
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.update", updated, "Success", ct);
        return updated;
    }

    private async Task DeleteSqlAsync(Guid tapeId, int expectedVersion, string actor, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await GetSqlAsync(c, tx, tapeId, null, ct) ?? throw NotFound();
        if (current.Version != expectedVersion) throw Conflict(current);
        await using var cmd = new SqlCommand("DELETE dbo.inv_tapes WHERE TapeId=@id AND Version=@expected;", c, tx);
        cmd.Parameters.AddWithValue("@id", tapeId);
        cmd.Parameters.AddWithValue("@expected", expectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
        {
            var latest = await GetSqlAsync(c, tx, tapeId, null, ct);
            throw Conflict(latest ?? current);
        }
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.delete", current, "Success", ct);
    }

    private async Task<IReadOnlyList<TapeFormatItem>> ListFormatsSqlAsync(bool includeInactive, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Code,NameEn,NameAr,IsActive,SortOrder FROM dbo.inv_tape_formats WHERE @all=1 OR IsActive=1 ORDER BY SortOrder,Code;", c);
        cmd.Parameters.AddWithValue("@all", includeInactive);
        var rows = new List<TapeFormatItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)));
        return rows;
    }

    private async Task<TapeFormatItem> UpsertFormatSqlAsync(UpsertTapeFormatRequest request, string actor, CancellationToken ct)
    {
        await using var c = await _sql!.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            MERGE dbo.inv_tape_formats AS t USING (SELECT @code Code) s ON t.Code=s.Code
            WHEN MATCHED THEN UPDATE SET NameEn=@en,NameAr=@ar,IsActive=@active,SortOrder=@sort,UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=@actor
            WHEN NOT MATCHED THEN INSERT(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy)
                VALUES(@code,@en,@ar,@active,@sort,SYSUTCDATETIME(),@actor);
            """, c);
        cmd.Parameters.AddWithValue("@code", request.Code);
        cmd.Parameters.AddWithValue("@en", request.NameEn);
        cmd.Parameters.AddWithValue("@ar", request.NameAr);
        cmd.Parameters.AddWithValue("@active", request.IsActive);
        cmd.Parameters.AddWithValue("@sort", request.SortOrder);
        cmd.Parameters.AddWithValue("@actor", actor);
        await cmd.ExecuteNonQueryAsync(ct);
        var result = new TapeFormatItem(request.Code, request.NameEn, request.NameAr, request.IsActive, request.SortOrder);
        await _audit.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "tape-format.upsert", "TapeFormat", request.Code, "Success"), ct);
        return result;
    }

    private async Task<TapeInventoryPage> ListDemoAsync(string? query, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var q = NormalizeOptional(query, 512);
        await using var c = await OpenDemoAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,OwnerDepartment,
                   DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy,
                   COUNT(*) OVER() TotalCount
            FROM DemoTape
            WHERE $q IS NULL OR TapeCode LIKE $like OR LegacyNumber LIKE $like OR Title LIKE $like OR Description LIKE $like
               OR TapeFormatCode LIKE $like OR PhysicalCondition LIKE $like OR DigitizationStatus LIKE $like
               OR OwnerDepartment LIKE $like OR Room LIKE $like OR Cabinet LIKE $like OR Shelf LIKE $like OR Bin LIKE $like OR Notes LIKE $like
            ORDER BY UpdatedAtUtc DESC,TapeCode DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$q", (object?)q ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$like", q is null ? DBNull.Value : $"%{q.Replace("%", "[%]", StringComparison.Ordinal)}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        var rows = new List<TapeInventoryItem>();
        var total = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (rows.Count == 0) total = r.GetInt32(21);
            rows.Add(ReadDemo(r));
        }
        return new(rows, total);
    }

    private async Task<TapeInventoryItem?> GetDemoAsync(Guid id, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        return await GetDemoAsync(c, null, id, null, ct);
    }

    private async Task<TapeInventoryItem?> ResolveDemoAsync(string code, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        return await GetDemoAsync(c, null, null, code, ct);
    }

    private async Task<TapeInventoryItem> CreateDemoAsync(CreateTapeRequest request, string actor, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        await using var tx = c.BeginTransaction();
        await EnsureFormatDemoAsync(c, tx, request.TapeFormatCode, ct);
        long sequence;
        await using (var counter = c.CreateCommand())
        {
            counter.Transaction = tx;
            counter.CommandText = "UPDATE DemoInventoryCounter SET LastValue=LastValue+1 WHERE CounterKey='tape' RETURNING LastValue;";
            sequence = Convert.ToInt64(await counter.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Demo tape counter was not initialized."));
        }
        var item = NewItem(request, TapeCode.FromSequence(sequence), actor);
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO DemoTape(TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,OwnerDepartment,
                    DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy)
                VALUES($id,$code,$legacy,$title,$description,$format,$condition,$status,$owner,$duration,$recordingDate,$room,$cabinet,$shelf,$bin,$notes,
                    1,$created,$actor,$created,$actor);
                """;
            AddDemoTapeParameters(cmd, item);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.create", item, "Success", ct);
        return item;
    }

    private async Task<TapeInventoryItem> UpdateDemoAsync(Guid tapeId, UpdateTapeRequest request, string actor, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        await using var tx = c.BeginTransaction();
        var current = await GetDemoAsync(c, tx, tapeId, null, ct) ?? throw NotFound();
        if (current.Version != request.ExpectedVersion) throw Conflict(current);
        await EnsureFormatDemoAsync(c, tx, request.TapeFormatCode, ct);
        var updated = Apply(current, request, actor);
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE DemoTape SET LegacyNumber=$legacy,Title=$title,Description=$description,TapeFormatCode=$format,PhysicalCondition=$condition,
                DigitizationStatus=$status,OwnerDepartment=$owner,DurationSeconds=$duration,RecordingDate=$recordingDate,Room=$room,Cabinet=$cabinet,
                Shelf=$shelf,Bin=$bin,Notes=$notes,Version=$version,UpdatedAtUtc=$updated,UpdatedBy=$actor
            WHERE TapeId=$id AND Version=$expected;
            """;
        AddDemoTapeParameters(cmd, updated);
        cmd.Parameters.AddWithValue("$expected", request.ExpectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw Conflict(current);
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.update", updated, "Success", ct);
        return updated;
    }

    private async Task DeleteDemoAsync(Guid tapeId, int expectedVersion, string actor, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        await using var tx = c.BeginTransaction();
        var current = await GetDemoAsync(c, tx, tapeId, null, ct) ?? throw NotFound();
        if (current.Version != expectedVersion) throw Conflict(current);
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM DemoTape WHERE TapeId=$id AND Version=$expected;";
        cmd.Parameters.AddWithValue("$id", tapeId.ToString("D"));
        cmd.Parameters.AddWithValue("$expected", expectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw Conflict(current);
        await tx.CommitAsync(ct);
        await AuditAsync(actor, "tape.delete", current, "Success", ct);
    }

    private async Task<IReadOnlyList<TapeFormatItem>> ListFormatsDemoAsync(bool includeInactive, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Code,NameEn,NameAr,IsActive,SortOrder FROM DemoTapeFormat WHERE $all=1 OR IsActive=1 ORDER BY SortOrder,Code;";
        cmd.Parameters.AddWithValue("$all", includeInactive ? 1 : 0);
        var rows = new List<TapeFormatItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3)==1,r.GetInt32(4)));
        return rows;
    }

    private async Task<TapeFormatItem> UpsertFormatDemoAsync(UpsertTapeFormatRequest request, string actor, CancellationToken ct)
    {
        await using var c = await OpenDemoAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DemoTapeFormat(Code,NameEn,NameAr,IsActive,SortOrder,UpdatedAtUtc,UpdatedBy)
            VALUES($code,$en,$ar,$active,$sort,$now,$actor)
            ON CONFLICT(Code) DO UPDATE SET NameEn=excluded.NameEn,NameAr=excluded.NameAr,IsActive=excluded.IsActive,
                SortOrder=excluded.SortOrder,UpdatedAtUtc=excluded.UpdatedAtUtc,UpdatedBy=excluded.UpdatedBy;
            """;
        cmd.Parameters.AddWithValue("$code", request.Code);
        cmd.Parameters.AddWithValue("$en", request.NameEn);
        cmd.Parameters.AddWithValue("$ar", request.NameAr);
        cmd.Parameters.AddWithValue("$active", request.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort", request.SortOrder);
        cmd.Parameters.AddWithValue("$now", DemoSqliteDatabase.ToDb(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue("$actor", actor);
        await cmd.ExecuteNonQueryAsync(ct);
        var result = new TapeFormatItem(request.Code, request.NameEn, request.NameAr, request.IsActive, request.SortOrder);
        await _audit.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "tape-format.upsert", "TapeFormat", request.Code, "Success"), ct);
        return result;
    }

    private async ValueTask<SqliteConnection> OpenDemoAsync(CancellationToken ct)
    {
        var c = await _demo!.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = DemoSchema;
        await cmd.ExecuteNonQueryAsync(ct);
        return c;
    }

    private static async Task<TapeInventoryItem?> GetSqlAsync(SqlConnection c, SqlTransaction? tx, Guid? id, string? code, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,OwnerDepartment,
                   DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy
            FROM dbo.inv_tapes WHERE (@id IS NOT NULL AND TapeId=@id) OR (@code IS NOT NULL AND TapeCode=@code);
            """, c, tx);
        cmd.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code", (object?)code ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadSql(r) : null;
    }

    private static async Task<TapeInventoryItem?> GetDemoAsync(SqliteConnection c, SqliteTransaction? tx, Guid? id, string? code, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT TapeId,TapeCode,LegacyNumber,Title,Description,TapeFormatCode,PhysicalCondition,DigitizationStatus,OwnerDepartment,
                   DurationSeconds,RecordingDate,Room,Cabinet,Shelf,Bin,Notes,Version,CreatedAtUtc,CreatedBy,UpdatedAtUtc,UpdatedBy
            FROM DemoTape WHERE ($id IS NOT NULL AND TapeId=$id) OR ($code IS NOT NULL AND TapeCode=$code);
            """;
        cmd.Parameters.AddWithValue("$id", id?.ToString("D") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$code", (object?)code ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadDemo(r) : null;
    }

    private static TapeInventoryItem ReadSql(SqlDataReader r) => new(
        r.GetGuid(0),r.GetString(1),N(r,2),N(r,3),N(r,4),N(r,5),N(r,6),r.GetString(7),N(r,8),NI(r,9),
        r.IsDBNull(10)?null:DateOnly.FromDateTime(r.GetDateTime(10)),N(r,11),N(r,12),N(r,13),N(r,14),N(r,15),r.GetInt32(16),
        new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(17),DateTimeKind.Utc)),r.GetString(18),
        new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(19),DateTimeKind.Utc)),r.GetString(20));

    private static TapeInventoryItem ReadDemo(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),r.GetString(1),N(r,2),N(r,3),N(r,4),N(r,5),N(r,6),r.GetString(7),N(r,8),NI(r,9),
        r.IsDBNull(10)?null:DateOnly.Parse(r.GetString(10),System.Globalization.CultureInfo.InvariantCulture),N(r,11),N(r,12),N(r,13),N(r,14),N(r,15),r.GetInt32(16),
        DemoSqliteDatabase.FromDb(r.GetString(17)),r.GetString(18),DemoSqliteDatabase.FromDb(r.GetString(19)),r.GetString(20));

    private static string? N(IDataRecord r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? NI(IDataRecord r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    private static void AddTapeParameters(SqlCommand cmd, TapeInventoryItem i)
    {
        cmd.Parameters.AddWithValue("@id", i.TapeId); cmd.Parameters.AddWithValue("@code", i.TapeCode);
        cmd.Parameters.AddWithValue("@legacy", Db(i.LegacyNumber)); cmd.Parameters.AddWithValue("@title", Db(i.Title));
        cmd.Parameters.AddWithValue("@description", Db(i.Description)); cmd.Parameters.AddWithValue("@format", Db(i.TapeFormatCode));
        cmd.Parameters.AddWithValue("@condition", Db(i.PhysicalCondition)); cmd.Parameters.AddWithValue("@status", i.DigitizationStatus);
        cmd.Parameters.AddWithValue("@owner", Db(i.OwnerDepartment)); cmd.Parameters.AddWithValue("@duration", (object?)i.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recordingDate", i.RecordingDate is null ? DBNull.Value : i.RecordingDate.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@room", Db(i.Room)); cmd.Parameters.AddWithValue("@cabinet", Db(i.Cabinet));
        cmd.Parameters.AddWithValue("@shelf", Db(i.Shelf)); cmd.Parameters.AddWithValue("@bin", Db(i.Bin)); cmd.Parameters.AddWithValue("@notes", Db(i.Notes));
        cmd.Parameters.AddWithValue("@version", i.Version); cmd.Parameters.AddWithValue("@created", i.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("@updated", i.UpdatedAtUtc.UtcDateTime); cmd.Parameters.AddWithValue("@actor", i.UpdatedBy);
    }

    private static void AddDemoTapeParameters(SqliteCommand cmd, TapeInventoryItem i)
    {
        cmd.Parameters.AddWithValue("$id", i.TapeId.ToString("D")); cmd.Parameters.AddWithValue("$code", i.TapeCode);
        cmd.Parameters.AddWithValue("$legacy", Db(i.LegacyNumber)); cmd.Parameters.AddWithValue("$title", Db(i.Title));
        cmd.Parameters.AddWithValue("$description", Db(i.Description)); cmd.Parameters.AddWithValue("$format", Db(i.TapeFormatCode));
        cmd.Parameters.AddWithValue("$condition", Db(i.PhysicalCondition)); cmd.Parameters.AddWithValue("$status", i.DigitizationStatus);
        cmd.Parameters.AddWithValue("$owner", Db(i.OwnerDepartment)); cmd.Parameters.AddWithValue("$duration", (object?)i.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$recordingDate", i.RecordingDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$room", Db(i.Room)); cmd.Parameters.AddWithValue("$cabinet", Db(i.Cabinet));
        cmd.Parameters.AddWithValue("$shelf", Db(i.Shelf)); cmd.Parameters.AddWithValue("$bin", Db(i.Bin)); cmd.Parameters.AddWithValue("$notes", Db(i.Notes));
        cmd.Parameters.AddWithValue("$version", i.Version); cmd.Parameters.AddWithValue("$created", DemoSqliteDatabase.ToDb(i.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$updated", DemoSqliteDatabase.ToDb(i.UpdatedAtUtc)); cmd.Parameters.AddWithValue("$actor", i.UpdatedBy);
    }

    private static object Db(string? value) => value is null ? DBNull.Value : value;

    private static async Task EnsureFormatSqlAsync(SqlConnection c, SqlTransaction tx, string? code, CancellationToken ct)
    {
        if (code is null) return;
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.inv_tape_formats WHERE Code=@code AND IsActive=1;", c, tx);
        cmd.Parameters.AddWithValue("@code", code);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw Bad("invalid_tape_format", "Tape format is not active or does not exist.");
    }

    private static async Task EnsureFormatDemoAsync(SqliteConnection c, SqliteTransaction tx, string? code, CancellationToken ct)
    {
        if (code is null) return;
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(1) FROM DemoTapeFormat WHERE Code=$code AND IsActive=1;"; cmd.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw Bad("invalid_tape_format", "Tape format is not active or does not exist.");
    }

    private async Task AuditAsync(string actor, string action, TapeInventoryItem item, string outcome, CancellationToken ct) =>
        await _audit.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, "Tape", item.TapeCode, outcome,
            $"TapeId={item.TapeId:D};Version={item.Version};Status={item.DigitizationStatus}"), ct);

    private static TapeInventoryItem NewItem(CreateTapeRequest r, string code, string actor)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(),code,r.LegacyNumber,r.Title,r.Description,r.TapeFormatCode,r.PhysicalCondition,TapeDigitizationStatuses.NotDigitized,
            r.OwnerDepartment,r.DurationSeconds,r.RecordingDate,r.Room,r.Cabinet,r.Shelf,r.Bin,r.Notes,1,now,actor,now,actor);
    }

    private static TapeInventoryItem Apply(TapeInventoryItem c, UpdateTapeRequest r, string actor) => c with
    {
        LegacyNumber=r.LegacyNumber,Title=r.Title,Description=r.Description,TapeFormatCode=r.TapeFormatCode,PhysicalCondition=r.PhysicalCondition,
        DigitizationStatus=r.DigitizationStatus,OwnerDepartment=r.OwnerDepartment,DurationSeconds=r.DurationSeconds,RecordingDate=r.RecordingDate,
        Room=r.Room,Cabinet=r.Cabinet,Shelf=r.Shelf,Bin=r.Bin,Notes=r.Notes,Version=c.Version+1,UpdatedAtUtc=DateTimeOffset.UtcNow,UpdatedBy=actor
    };

    private static CreateTapeRequest ValidateCreate(CreateTapeRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r with
        {
            LegacyNumber=NormalizeOptional(r.LegacyNumber,128),Title=NormalizeOptional(r.Title,512),Description=NormalizeOptional(r.Description,4000),
            TapeFormatCode=NormalizeNullableFormatCode(r.TapeFormatCode),PhysicalCondition=NormalizeCondition(r.PhysicalCondition),
            OwnerDepartment=NormalizeOptional(r.OwnerDepartment,256),Room=NormalizeOptional(r.Room,128),Cabinet=NormalizeOptional(r.Cabinet,128),
            Shelf=NormalizeOptional(r.Shelf,128),Bin=NormalizeOptional(r.Bin,128),Notes=NormalizeOptional(r.Notes,4000)
        };
    }

    private static UpdateTapeRequest ValidateUpdate(UpdateTapeRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.ExpectedVersion <= 0) throw Bad("expected_version_required", "ExpectedVersion must be greater than zero.");
        var status = r.DigitizationStatus?.Trim() ?? string.Empty;
        if (!TapeDigitizationStatuses.All.Contains(status)) throw Bad("invalid_digitization_status", "Digitization status is invalid.");
        return r with
        {
            LegacyNumber=NormalizeOptional(r.LegacyNumber,128),Title=NormalizeOptional(r.Title,512),Description=NormalizeOptional(r.Description,4000),
            TapeFormatCode=NormalizeNullableFormatCode(r.TapeFormatCode),PhysicalCondition=NormalizeCondition(r.PhysicalCondition),DigitizationStatus=status,
            OwnerDepartment=NormalizeOptional(r.OwnerDepartment,256),Room=NormalizeOptional(r.Room,128),Cabinet=NormalizeOptional(r.Cabinet,128),
            Shelf=NormalizeOptional(r.Shelf,128),Bin=NormalizeOptional(r.Bin,128),Notes=NormalizeOptional(r.Notes,4000)
        };
    }

    private static string? NormalizeCondition(string? value)
    {
        var v = NormalizeOptional(value,32);
        if (v is not null && !TapePhysicalConditions.All.Contains(v)) throw Bad("invalid_physical_condition", "Physical condition is invalid.");
        return v;
    }

    private static string? NormalizeNullableFormatCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeFormatCode(value);
    private static string NormalizeFormatCode(string value)
    {
        var v = Required(value,64,"format_code_required").ToUpperInvariant().Replace(' ','_');
        if (v.Any(ch => !(char.IsLetterOrDigit(ch) || ch=='_' || ch=='-'))) throw Bad("invalid_format_code", "Tape format code may contain only letters, digits, underscore and hyphen.");
        return v;
    }
    private static string NormalizeCode(string value)
    {
        var raw = Required(value,2048,"tape_code_required");
        var extracted = TapeBarcodePayload.ExtractTapeCode(raw);
        var v = (extracted ?? raw).ToUpperInvariant();
        if (!v.StartsWith(TapeCode.Prefix,StringComparison.Ordinal) || v.Length != TapeCode.Prefix.Length + TapeCode.SequenceDigits || !v[TapeCode.Prefix.Length..].All(char.IsDigit))
            throw Bad("invalid_tape_code", "Tape scan must contain a TAPE-###### identity.");
        return v;
    }
    private static string Actor(string actor) => string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim()[..Math.Min(actor.Trim().Length,256)];
    private static string Required(string? value,int max,string code) => NormalizeOptional(value,max) ?? throw Bad(code,"A required value is missing.");
    private static string? NormalizeOptional(string? value,int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v=value.Trim();
        if (v.Length>max) throw Bad("value_too_long",$"Value exceeds {max} characters.");
        return v;
    }
    private static string EscapeLike(string value) => value.Replace("[","[[]",StringComparison.Ordinal).Replace("%","[%]",StringComparison.Ordinal).Replace("_","[_]",StringComparison.Ordinal);
    private static TapeInventoryRequestException Bad(string code,string message) => new(code,message,400);
    private static TapeInventoryRequestException NotFound() => new("tape_not_found","Tape was not found.",404);
    private static TapeInventoryRequestException Conflict(TapeInventoryItem current) => new("tape_version_conflict","Tape was changed by another user. Reload and retry.",409,current);

    private const string DemoSchema = """
        CREATE TABLE IF NOT EXISTS DemoInventoryCounter(CounterKey TEXT PRIMARY KEY,LastValue INTEGER NOT NULL CHECK(LastValue>=0));
        INSERT OR IGNORE INTO DemoInventoryCounter(CounterKey,LastValue) VALUES('tape',0);
        CREATE TABLE IF NOT EXISTS DemoTapeFormat(Code TEXT PRIMARY KEY,NameEn TEXT NOT NULL,NameAr TEXT NOT NULL,IsActive INTEGER NOT NULL,SortOrder INTEGER NOT NULL,UpdatedAtUtc TEXT NOT NULL,UpdatedBy TEXT NOT NULL);
        INSERT OR IGNORE INTO DemoTapeFormat VALUES('HDCAM','HDCAM','HDCAM',1,10,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoTapeFormat VALUES('BETACAM','Betacam','Betacam',1,20,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoTapeFormat VALUES('BETACAM_SP','Betacam SP','Betacam SP',1,30,'2000-01-01T00:00:00.0000000Z','seed');
        INSERT OR IGNORE INTO DemoTapeFormat VALUES('DIGITAL_BETACAM','Digital Betacam','Digital Betacam',1,40,'2000-01-01T00:00:00.0000000Z','seed');
        CREATE TABLE IF NOT EXISTS DemoTape(
            TapeId TEXT PRIMARY KEY,TapeCode TEXT NOT NULL UNIQUE,LegacyNumber TEXT NULL,Title TEXT NULL,Description TEXT NULL,TapeFormatCode TEXT NULL,
            PhysicalCondition TEXT NULL,DigitizationStatus TEXT NOT NULL,OwnerDepartment TEXT NULL,DurationSeconds INTEGER NULL,RecordingDate TEXT NULL,
            Room TEXT NULL,Cabinet TEXT NULL,Shelf TEXT NULL,Bin TEXT NULL,Notes TEXT NULL,Version INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,UpdatedBy TEXT NOT NULL,
            FOREIGN KEY(TapeFormatCode) REFERENCES DemoTapeFormat(Code));
        CREATE INDEX IF NOT EXISTS IX_DemoTape_Updated ON DemoTape(UpdatedAtUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_DemoTape_Legacy ON DemoTape(LegacyNumber);
        """;
}
