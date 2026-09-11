using MAM.Application.Auditing;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Auditing;

public sealed class SqlServerAuditSink : IAuditSink
{
    private readonly Catalog.SqlServerConnectionFactory _connections;

    public SqlServerAuditSink(Catalog.SqlServerConnectionFactory connections)
    {
        _connections = connections;
    }

    public async ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            INSERT dbo.MamAuditEvent
                (AuditEventId, OccurredAtUtc, ActorId, Action, EntityType, EntityId, Outcome, Detail)
            VALUES
                (@Id, @OccurredAtUtc, @ActorId, @Action, @EntityType, @EntityId, @Outcome, @Detail);
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Id", auditEvent.Id);
        command.Parameters.AddWithValue("@OccurredAtUtc", auditEvent.OccurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@ActorId", auditEvent.ActorId);
        command.Parameters.AddWithValue("@Action", auditEvent.Action);
        command.Parameters.AddWithValue("@EntityType", auditEvent.EntityType);
        command.Parameters.AddWithValue("@EntityId", auditEvent.EntityId);
        command.Parameters.AddWithValue("@Outcome", auditEvent.Outcome);
        command.Parameters.AddWithValue("@Detail", (object?)auditEvent.Detail ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (@Limit)
                AuditEventId, OccurredAtUtc, ActorId, Action, EntityType, EntityId, Outcome, Detail
            FROM dbo.MamAuditEvent
            ORDER BY OccurredAtUtc DESC, AuditEventId DESC;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _connections.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AuditEvent(
                reader.GetGuid(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return events;
    }
}
