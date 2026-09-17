using System.Data;
using MAM.Application.Auditing;
using MAM.Application.TapeInventory;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.TapeInventory;

public sealed class SqlServerTapeInventoryService : ITapeInventoryService
{
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuditSink _audit;

    public SqlServerTapeInventoryService(SqlServerConnectionFactory connections, IAuditSink audit)
    {
        _connections = connections;
        _audit = audit;
    }

    public async ValueTask<IReadOnlyList<TapeInventoryRecord>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var normalized = NormalizeOptional(query, 300);
        const string sql = """
            SELECT TapeId,TapeCode,Title,Description,LegacyNumber,TapeFormat,PhysicalCondition,DigitizationStatus,
                   Room,Cabinet,Shelf,Bin,OwnerDepartment,Notes,Version,CreatedAtUtc,UpdatedAtUtc
            FROM dbo.MamTape
            WHERE @Query IS NULL
               OR TapeCode LIKE N'%' + @Query + N'%'
               OR Title LIKE N'%' + @Query + N'%'
               OR LegacyNumber LIKE N'%' + @Query + N'%'
               OR Description LIKE N'%' + @Query + N'%'
            ORDER BY TapeNumber DESC;
            """;
        await using var command = NewCommand(sql, connection);
        command.Parameters.Add("@Query", SqlDbType.NVarChar, 300).Value = (object?)normalized ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<TapeInventoryRecord>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return items;
    }

    public async ValueTask<TapeInventoryRecord?> GetAsync(Guid tapeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await GetAsync(connection, tapeId, cancellationToken);
    }

    public async ValueTask<TapeInventoryRecord?> GetByCodeAsync(string tapeCode, CancellationToken cancellationToken = default)
    {
        var code = NormalizeRequired(tapeCode, 40, "Tape code").ToUpperInvariant();
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TapeId,TapeCode,Title,Description,LegacyNumber,TapeFormat,PhysicalCondition,DigitizationStatus,
                   Room,Cabinet,Shelf,Bin,OwnerDepartment,Notes,Version,CreatedAtUtc,UpdatedAtUtc
            FROM dbo.MamTape WHERE TapeCode=@TapeCode;
            """;
        await using var command = NewCommand(sql, connection);
        command.Parameters.Add("@TapeCode", SqlDbType.NVarChar, 40).Value = code;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<TapeMutationResult> CreateAsync(CreateTapeRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(request, out var input, out var error))
            return new TapeMutationResult(TapeMutationStatus.Invalid, Error: error);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var tapeId = Guid.NewGuid();
        try
        {
            const string allocateSql = """
                UPDATE dbo.MamTapeCounter WITH (UPDLOCK,HOLDLOCK)
                SET NextValue=NextValue+1
                OUTPUT deleted.NextValue
                WHERE CounterKey=N'TAPE';
                """;
            long number;
            await using (var allocate = NewCommand(allocateSql, connection, transaction))
            {
                var value = await allocate.ExecuteScalarAsync(cancellationToken);
                if (value is null || value is DBNull) throw new InvalidOperationException("Tape counter is missing.");
                number = Convert.ToInt64(value);
            }
            if (number > 999999) throw new InvalidOperationException("Tape number range TAPE-000001..TAPE-999999 is exhausted.");

            const string insertSql = """
                INSERT dbo.MamTape
                (TapeId,TapeNumber,Title,Description,LegacyNumber,TapeFormat,PhysicalCondition,DigitizationStatus,
                 Room,Cabinet,Shelf,Bin,OwnerDepartment,Notes,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES
                (@TapeId,@TapeNumber,@Title,@Description,@LegacyNumber,@TapeFormat,@PhysicalCondition,@DigitizationStatus,
                 @Room,@Cabinet,@Shelf,@Bin,@OwnerDepartment,@Notes,1,SYSUTCDATETIME(),SYSUTCDATETIME());
                """;
            await using (var insert = NewCommand(insertSql, connection, transaction))
            {
                Bind(insert, input);
                insert.Parameters.Add("@TapeId", SqlDbType.UniqueIdentifier).Value = tapeId;
                insert.Parameters.Add("@TapeNumber", SqlDbType.BigInt).Value = number;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new TapeMutationResult(TapeMutationStatus.Unavailable, Error: ex.Message);
        }

        var created = await GetAsync(connection, tapeId, cancellationToken);
        if (created is null) return new TapeMutationResult(TapeMutationStatus.Unavailable, Error: "Created tape could not be re-read.");
        await _audit.AppendAsync(NewAudit(actorId, "tape.inventory.created", created, "Success", $"code={created.TapeCode};version=1"), cancellationToken);
        return new TapeMutationResult(TapeMutationStatus.Created, created);
    }

    public async ValueTask<TapeMutationResult> UpdateAsync(Guid tapeId, UpdateTapeRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (request.ExpectedVersion < 1)
            return new TapeMutationResult(TapeMutationStatus.Invalid, Error: "ExpectedVersion must be at least 1.");
        if (!TryNormalize(request, out var input, out var error))
            return new TapeMutationResult(TapeMutationStatus.Invalid, Error: error);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamTape
            SET Title=@Title,Description=@Description,LegacyNumber=@LegacyNumber,TapeFormat=@TapeFormat,
                PhysicalCondition=@PhysicalCondition,DigitizationStatus=@DigitizationStatus,Room=@Room,
                Cabinet=@Cabinet,Shelf=@Shelf,Bin=@Bin,OwnerDepartment=@OwnerDepartment,Notes=@Notes,
                Version=Version+1,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE TapeId=@TapeId AND Version=@ExpectedVersion;
            """;
        await using (var command = NewCommand(sql, connection))
        {
            Bind(command, input);
            command.Parameters.Add("@TapeId", SqlDbType.UniqueIdentifier).Value = tapeId;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.BigInt).Value = request.ExpectedVersion;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            var current = await GetAsync(connection, tapeId, cancellationToken);
            if (affected == 0)
            {
                if (current is null) return new TapeMutationResult(TapeMutationStatus.NotFound);
                await _audit.AppendAsync(NewAudit(actorId, "tape.inventory.update-conflict", current, "Conflict", $"expected={request.ExpectedVersion};current={current.Version}"), cancellationToken);
                return new TapeMutationResult(TapeMutationStatus.Conflict, current, "The tape was changed by another request. Refresh and retry.");
            }

            if (current is null) return new TapeMutationResult(TapeMutationStatus.Unavailable, Error: "Updated tape could not be re-read.");
            await _audit.AppendAsync(NewAudit(actorId, "tape.inventory.updated", current, "Success", $"version={current.Version}"), cancellationToken);
            return new TapeMutationResult(TapeMutationStatus.Updated, current);
        }
    }

    public async ValueTask<TapeInventoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.MamSchemaVersion WHERE MigrationId=N'0014_t21_tape_inventory';";
            await using var command = NewCommand(sql, connection);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count == 1
                ? new TapeInventoryHealth(true, "SqlServer", "T2.1 tape inventory schema is reachable.")
                : new TapeInventoryHealth(false, "SqlServer", "SQL Server is reachable but migration 0014_t21_tape_inventory is not applied.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return new TapeInventoryHealth(false, "SqlServer", $"Tape inventory is unavailable: {ex.GetType().Name}.");
        }
    }

    private async ValueTask<TapeInventoryRecord?> GetAsync(SqlConnection connection, Guid tapeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TapeId,TapeCode,Title,Description,LegacyNumber,TapeFormat,PhysicalCondition,DigitizationStatus,
                   Room,Cabinet,Shelf,Bin,OwnerDepartment,Notes,Version,CreatedAtUtc,UpdatedAtUtc
            FROM dbo.MamTape WHERE TapeId=@TapeId;
            """;
        await using var command = NewCommand(sql, connection);
        command.Parameters.Add("@TapeId", SqlDbType.UniqueIdentifier).Value = tapeId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private SqlCommand NewCommand(string sql, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = _connections.CommandTimeoutSeconds };

    private static TapeInventoryRecord Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), NullableString(reader,3), NullableString(reader,4),
        NullableString(reader,5), NullableString(reader,6), reader.GetString(7), NullableString(reader,8), NullableString(reader,9),
        NullableString(reader,10), NullableString(reader,11), NullableString(reader,12), NullableString(reader,13), reader.GetInt64(14),
        Utc(reader.GetDateTime(15)), Utc(reader.GetDateTime(16)));

    private static string? NullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool TryNormalize(CreateTapeRequest request, out Normalized input, out string? error) =>
        TryNormalizeCore(request.Title,request.Description,request.LegacyNumber,request.TapeFormat,request.PhysicalCondition,
            request.DigitizationStatus ?? TapeDigitizationStatuses.NotDigitized,request.Room,request.Cabinet,request.Shelf,request.Bin,
            request.OwnerDepartment,request.Notes,out input,out error);

    private static bool TryNormalize(UpdateTapeRequest request, out Normalized input, out string? error) =>
        TryNormalizeCore(request.Title,request.Description,request.LegacyNumber,request.TapeFormat,request.PhysicalCondition,
            request.DigitizationStatus,request.Room,request.Cabinet,request.Shelf,request.Bin,request.OwnerDepartment,request.Notes,out input,out error);

    private static bool TryNormalizeCore(string title,string? description,string? legacyNumber,string? tapeFormat,string? physicalCondition,
        string digitizationStatus,string? room,string? cabinet,string? shelf,string? bin,string? ownerDepartment,string? notes,
        out Normalized input,out string? error)
    {
        try
        {
            var status = NormalizeRequired(digitizationStatus,50,"Digitization status");
            if (!TapeDigitizationStatuses.All.Contains(status)) throw new ArgumentException("Unsupported digitization status.");
            input = new Normalized(
                NormalizeRequired(title,300,"Title"),NormalizeOptional(description,2000),NormalizeOptional(legacyNumber,200),
                NormalizeOptional(tapeFormat,100),NormalizeOptional(physicalCondition,100),status,NormalizeOptional(room,100),
                NormalizeOptional(cabinet,100),NormalizeOptional(shelf,100),NormalizeOptional(bin,100),
                NormalizeOptional(ownerDepartment,200),NormalizeOptional(notes,2000));
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            input = default!;
            error = ex.Message;
            return false;
        }
    }

    private static void Bind(SqlCommand command, Normalized value)
    {
        command.Parameters.Add("@Title", SqlDbType.NVarChar,300).Value=value.Title;
        AddNullable(command,"@Description",2000,value.Description); AddNullable(command,"@LegacyNumber",200,value.LegacyNumber);
        AddNullable(command,"@TapeFormat",100,value.TapeFormat); AddNullable(command,"@PhysicalCondition",100,value.PhysicalCondition);
        command.Parameters.Add("@DigitizationStatus",SqlDbType.NVarChar,50).Value=value.DigitizationStatus;
        AddNullable(command,"@Room",100,value.Room); AddNullable(command,"@Cabinet",100,value.Cabinet); AddNullable(command,"@Shelf",100,value.Shelf);
        AddNullable(command,"@Bin",100,value.Bin); AddNullable(command,"@OwnerDepartment",200,value.OwnerDepartment); AddNullable(command,"@Notes",2000,value.Notes);
    }

    private static void AddNullable(SqlCommand command,string name,int size,string? value) =>
        command.Parameters.Add(name,SqlDbType.NVarChar,size).Value=(object?)value ?? DBNull.Value;

    private static string NormalizeRequired(string? value,int max,string label)
    {
        var normalized=value?.Trim() ?? string.Empty;
        if (normalized.Length==0) throw new ArgumentException($"{label} is required.");
        if (normalized.Length>max) throw new ArgumentException($"{label} cannot exceed {max} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value,int max)
    {
        var normalized=value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length>max) throw new ArgumentException($"Value cannot exceed {max} characters.");
        return normalized;
    }

    private static AuditEvent NewAudit(string actorId,string action,TapeInventoryRecord tape,string outcome,string? detail) =>
        new(Guid.NewGuid(),DateTimeOffset.UtcNow,actorId,action,"MamTape",tape.TapeId.ToString("D"),outcome,detail);

    private sealed record Normalized(string Title,string? Description,string? LegacyNumber,string? TapeFormat,string? PhysicalCondition,
        string DigitizationStatus,string? Room,string? Cabinet,string? Shelf,string? Bin,string? OwnerDepartment,string? Notes);
}
