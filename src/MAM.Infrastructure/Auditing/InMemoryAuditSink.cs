using System.Collections.Concurrent;
using MAM.Application.Auditing;

namespace MAM.Infrastructure.Auditing;

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(auditEvent);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boundedLimit = Math.Clamp(limit, 1, 200);
        IReadOnlyList<AuditEvent> result = _events.Reverse().Take(boundedLimit).ToArray();
        return ValueTask.FromResult(result);
    }
}
