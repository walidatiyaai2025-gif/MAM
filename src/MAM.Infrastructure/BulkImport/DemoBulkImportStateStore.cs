using MAM.Application.BulkImport;
using MAM.Infrastructure.Demo;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.BulkImport;

public sealed class DemoBulkImportStateStore(DemoSqliteDatabase database) : IBulkImportStateStore
{
    public async Task CreateAsync(BulkImportSessionRow session, IReadOnlyList<BulkImportItemRow> items, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO DemoBulkImportSession
                (SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc)
                VALUES($session,$root,$state,$files,$bytes,$actor,$created,$updated,$completed);
                """;
            BindSession(command, session);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO DemoBulkImportItem
                (ItemId,SessionId,RelativePath,FileName,CategoryName,CategoryId,ExpectedLength,ExpectedSha256,
                 UploadSessionId,AssetId,State,ReasonCode,Detail,UpdatedAtUtc)
                VALUES($item,$session,$relative,$file,$category,$categoryId,$length,$sha,$upload,$asset,$state,$reason,$detail,$updated);
                """;
            BindItem(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<(BulkImportSessionRow Session, IReadOnlyList<BulkImportItemRow> Items)?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var sessionCommand = connection.CreateCommand();
        sessionCommand.CommandText = """
            SELECT SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM DemoBulkImportSession WHERE SessionId=$session;
            """;
        sessionCommand.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        await using var reader = await sessionCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var session = ReadSession(reader);
        await reader.DisposeAsync();

        await using var itemCommand = connection.CreateCommand();
        itemCommand.CommandText = """
            SELECT ItemId,SessionId,RelativePath,FileName,CategoryName,CategoryId,ExpectedLength,ExpectedSha256,
                   UploadSessionId,AssetId,State,ReasonCode,Detail,UpdatedAtUtc
            FROM DemoBulkImportItem WHERE SessionId=$session ORDER BY RelativePath,ItemId;
            """;
        itemCommand.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
        var items = new List<BulkImportItemRow>();
        while (await itemReader.ReadAsync(cancellationToken)) items.Add(ReadItem(itemReader));
        return (session, items);
    }

    public async Task<IReadOnlyList<BulkImportSessionRow>> ListRecentAsync(string createdBy, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM DemoBulkImportSession
            WHERE CreatedBy=$actor
            ORDER BY UpdatedAtUtc DESC,SessionId DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$actor", createdBy);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<BulkImportSessionRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSession(reader));
        return rows;
    }

    public async Task UpdateSessionAsync(BulkImportSessionRow session, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DemoBulkImportSession
            SET RootFolderName=$root,State=$state,TotalFiles=$files,TotalBytes=$bytes,
                UpdatedAtUtc=$updated,CompletedAtUtc=$completed
            WHERE SessionId=$session AND CreatedBy=$actor;
            """;
        BindSession(command, session);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Bulk import session changed or disappeared while updating.");
    }

    public async Task UpdateItemAsync(BulkImportItemRow item, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DemoBulkImportItem
            SET CategoryName=$category,CategoryId=$categoryId,ExpectedLength=$length,ExpectedSha256=$sha,
                UploadSessionId=$upload,AssetId=$asset,State=$state,ReasonCode=$reason,Detail=$detail,UpdatedAtUtc=$updated
            WHERE ItemId=$item AND SessionId=$session;
            """;
        BindItem(command, item);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Bulk import item changed or disappeared while updating.");
    }

    private static void BindSession(SqliteCommand command, BulkImportSessionRow session)
    {
        command.Parameters.AddWithValue("$session", session.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$root", session.RootFolderName);
        command.Parameters.AddWithValue("$state", (int)session.State);
        command.Parameters.AddWithValue("$files", session.TotalFiles);
        command.Parameters.AddWithValue("$bytes", session.TotalBytes);
        command.Parameters.AddWithValue("$actor", session.CreatedBy);
        command.Parameters.AddWithValue("$created", DemoSqliteDatabase.ToDb(session.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", DemoSqliteDatabase.ToDb(session.UpdatedAtUtc));
        command.Parameters.AddWithValue("$completed", session.CompletedAtUtc is DateTimeOffset completed ? DemoSqliteDatabase.ToDb(completed) : DBNull.Value);
    }

    private static void BindItem(SqliteCommand command, BulkImportItemRow item)
    {
        command.Parameters.AddWithValue("$item", item.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$session", item.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$relative", item.RelativePath);
        command.Parameters.AddWithValue("$file", item.FileName);
        command.Parameters.AddWithValue("$category", item.CategoryName);
        command.Parameters.AddWithValue("$categoryId", item.CategoryId is Guid categoryId ? categoryId.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$length", item.ExpectedLength);
        command.Parameters.AddWithValue("$sha", item.ExpectedSha256 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$upload", item.UploadSessionId is Guid uploadId ? uploadId.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$asset", item.AssetId is Guid assetId ? assetId.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$state", (int)item.State);
        command.Parameters.AddWithValue("$reason", item.ReasonCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$detail", item.Detail ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updated", DemoSqliteDatabase.ToDb(item.UpdatedAtUtc));
    }

    private static BulkImportSessionRow ReadSession(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        (BulkImportSessionState)reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt64(4),
        reader.GetString(5),
        DemoSqliteDatabase.FromDb(reader.GetString(6)),
        DemoSqliteDatabase.FromDb(reader.GetString(7)),
        reader.IsDBNull(8) ? null : DemoSqliteDatabase.FromDb(reader.GetString(8)));

    private static BulkImportItemRow ReadItem(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
        reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
        (BulkImportItemState)reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        DemoSqliteDatabase.FromDb(reader.GetString(13)));
}
