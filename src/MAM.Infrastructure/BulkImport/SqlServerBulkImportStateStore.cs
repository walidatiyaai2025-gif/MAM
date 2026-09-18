using System.Data;
using MAM.Application.BulkImport;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.BulkImport;

public sealed class SqlServerBulkImportStateStore(SqlServerConnectionFactory connections) : IBulkImportStateStore
{
    public async Task CreateAsync(BulkImportSessionRow session, IReadOnlyList<BulkImportItemRow> items, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        const string sessionSql = """
            INSERT dbo.MamBulkImportSession
            (SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc)
            VALUES(@SessionId,@RootFolderName,@State,@TotalFiles,@TotalBytes,@CreatedBy,@CreatedAtUtc,@UpdatedAtUtc,@CompletedAtUtc);
            """;
        await using (var command = new SqlCommand(sessionSql, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds })
        {
            BindSession(command, session);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string itemSql = """
            INSERT dbo.MamBulkImportItem
            (ItemId,SessionId,RelativePath,FileName,CategoryName,CategoryId,ExpectedLength,ExpectedSha256,
             UploadSessionId,AssetId,State,ReasonCode,Detail,UpdatedAtUtc)
            VALUES
            (@ItemId,@SessionId,@RelativePath,@FileName,@CategoryName,@CategoryId,@ExpectedLength,@ExpectedSha256,
             @UploadSessionId,@AssetId,@State,@ReasonCode,@Detail,@UpdatedAtUtc);
            """;
        foreach (var item in items)
        {
            await using var command = new SqlCommand(itemSql, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds };
            BindItem(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<(BulkImportSessionRow Session, IReadOnlyList<BulkImportItemRow> Items)?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        const string sessionSql = """
            SELECT SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM dbo.MamBulkImportSession WHERE SessionId=@SessionId;
            """;
        await using var sessionCommand = new SqlCommand(sessionSql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
        sessionCommand.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        await using var reader = await sessionCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var session = ReadSession(reader);
        await reader.DisposeAsync();

        const string itemSql = """
            SELECT ItemId,SessionId,RelativePath,FileName,CategoryName,CategoryId,ExpectedLength,ExpectedSha256,
                   UploadSessionId,AssetId,State,ReasonCode,Detail,UpdatedAtUtc
            FROM dbo.MamBulkImportItem WHERE SessionId=@SessionId ORDER BY RelativePath,ItemId;
            """;
        await using var itemCommand = new SqlCommand(itemSql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
        itemCommand.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
        await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
        var items = new List<BulkImportItemRow>();
        while (await itemReader.ReadAsync(cancellationToken)) items.Add(ReadItem(itemReader));
        return (session, items);
    }

    public async Task<IReadOnlyList<BulkImportSessionRow>> ListRecentAsync(string createdBy, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (@Limit) SessionId,RootFolderName,State,TotalFiles,TotalBytes,CreatedBy,CreatedAtUtc,UpdatedAtUtc,CompletedAtUtc
            FROM dbo.MamBulkImportSession
            WHERE CreatedBy=@CreatedBy
            ORDER BY UpdatedAtUtc DESC,SessionId DESC;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, 100);
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 200).Value = createdBy;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<BulkImportSessionRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadSession(reader));
        return rows;
    }

    public async Task UpdateSessionAsync(BulkImportSessionRow session, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamBulkImportSession
            SET RootFolderName=@RootFolderName,State=@State,TotalFiles=@TotalFiles,TotalBytes=@TotalBytes,
                UpdatedAtUtc=@UpdatedAtUtc,CompletedAtUtc=@CompletedAtUtc
            WHERE SessionId=@SessionId AND CreatedBy=@CreatedBy;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
        BindSession(command, session);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Bulk import session changed or disappeared while updating.");
    }

    public async Task UpdateItemAsync(BulkImportItemRow item, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.MamBulkImportItem
            SET CategoryName=@CategoryName,CategoryId=@CategoryId,ExpectedLength=@ExpectedLength,ExpectedSha256=@ExpectedSha256,
                UploadSessionId=@UploadSessionId,AssetId=@AssetId,State=@State,ReasonCode=@ReasonCode,Detail=@Detail,UpdatedAtUtc=@UpdatedAtUtc
            WHERE ItemId=@ItemId AND SessionId=@SessionId;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
        BindItem(command, item);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Bulk import item changed or disappeared while updating.");
    }

    private static void BindSession(SqlCommand command, BulkImportSessionRow session)
    {
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
        command.Parameters.Add("@RootFolderName", SqlDbType.NVarChar, 200).Value = session.RootFolderName;
        command.Parameters.Add("@State", SqlDbType.Int).Value = (int)session.State;
        command.Parameters.Add("@TotalFiles", SqlDbType.Int).Value = session.TotalFiles;
        command.Parameters.Add("@TotalBytes", SqlDbType.BigInt).Value = session.TotalBytes;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 200).Value = session.CreatedBy;
        command.Parameters.Add("@CreatedAtUtc", SqlDbType.DateTime2).Value = session.CreatedAtUtc.UtcDateTime;
        command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = session.UpdatedAtUtc.UtcDateTime;
        command.Parameters.Add("@CompletedAtUtc", SqlDbType.DateTime2).Value = (object?)session.CompletedAtUtc?.UtcDateTime ?? DBNull.Value;
    }

    private static void BindItem(SqlCommand command, BulkImportItemRow item)
    {
        command.Parameters.Add("@ItemId", SqlDbType.UniqueIdentifier).Value = item.ItemId;
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = item.SessionId;
        command.Parameters.Add("@RelativePath", SqlDbType.NVarChar, 1000).Value = item.RelativePath;
        command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = item.FileName;
        command.Parameters.Add("@CategoryName", SqlDbType.NVarChar, 200).Value = item.CategoryName;
        command.Parameters.Add("@CategoryId", SqlDbType.UniqueIdentifier).Value = (object?)item.CategoryId ?? DBNull.Value;
        command.Parameters.Add("@ExpectedLength", SqlDbType.BigInt).Value = item.ExpectedLength;
        command.Parameters.Add("@ExpectedSha256", SqlDbType.Char, 64).Value = (object?)item.ExpectedSha256 ?? DBNull.Value;
        command.Parameters.Add("@UploadSessionId", SqlDbType.UniqueIdentifier).Value = (object?)item.UploadSessionId ?? DBNull.Value;
        command.Parameters.Add("@AssetId", SqlDbType.UniqueIdentifier).Value = (object?)item.AssetId ?? DBNull.Value;
        command.Parameters.Add("@State", SqlDbType.Int).Value = (int)item.State;
        command.Parameters.Add("@ReasonCode", SqlDbType.NVarChar, 100).Value = (object?)item.ReasonCode ?? DBNull.Value;
        command.Parameters.Add("@Detail", SqlDbType.NVarChar, 1000).Value = (object?)item.Detail ?? DBNull.Value;
        command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = item.UpdatedAtUtc.UtcDateTime;
    }

    private static BulkImportSessionRow ReadSession(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        (BulkImportSessionState)reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt64(4),
        reader.GetString(5),
        Utc(reader.GetDateTime(6)),
        Utc(reader.GetDateTime(7)),
        reader.IsDBNull(8) ? null : Utc(reader.GetDateTime(8)));

    private static BulkImportItemRow ReadItem(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
        reader.IsDBNull(8) ? null : reader.GetGuid(8),
        reader.IsDBNull(9) ? null : reader.GetGuid(9),
        (BulkImportItemState)reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        Utc(reader.GetDateTime(13)));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
