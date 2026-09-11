namespace MAM.Application.Auditing;

public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ActorId,
    string Action,
    string EntityType,
    string EntityId,
    string Outcome,
    string? Detail = null);

public interface IAuditSink
{
    ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
