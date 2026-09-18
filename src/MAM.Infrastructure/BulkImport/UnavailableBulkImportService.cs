using MAM.Application.BulkImport;

namespace MAM.Infrastructure.BulkImport;

public sealed class UnavailableBulkImportService(string detail) : IBulkImportService
{
    private BulkImportRequestException Error() => new("bulk_import_unavailable", detail, 503);

    public Task<BulkImportSessionSnapshot> CreateSessionAsync(CreateBulkImportSessionRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportSessionSnapshot>(Error());
    public Task<BulkImportSessionSnapshot> GetSessionAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportSessionSnapshot>(Error());
    public Task<IReadOnlyList<BulkImportSessionSummary>> ListRecentAsync(string actorId, int limit = 20, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<BulkImportSessionSummary>>(Error());
    public Task<BulkImportItemSnapshot> BeginItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportItemSnapshot>(Error());
    public Task<BulkImportItemSnapshot> FinalizeItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportItemSnapshot>(Error());
    public Task<BulkImportItemSnapshot> FailItemAsync(Guid sessionId, Guid itemId, BulkImportFailureRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportItemSnapshot>(Error());
    public Task<BulkImportItemSnapshot> RetryItemAsync(Guid sessionId, Guid itemId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportItemSnapshot>(Error());
    public Task<BulkImportSessionSnapshot> CancelAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportSessionSnapshot>(Error());
    public Task<BulkImportReport> GetReportAsync(Guid sessionId, string format, string actorId, CancellationToken cancellationToken = default) => Task.FromException<BulkImportReport>(Error());
}
