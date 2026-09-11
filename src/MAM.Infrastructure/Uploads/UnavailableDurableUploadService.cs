using MAM.Application.Uploads;

namespace MAM.Infrastructure.Uploads;

public sealed class UnavailableDurableUploadService : IDurableUploadService
{
    private readonly string _detail;

    public UnavailableDurableUploadService(string detail)
    {
        _detail = string.IsNullOrWhiteSpace(detail) ? "Durable upload service is unavailable." : detail;
    }

    public ValueTask<UploadSessionSnapshot> CreateSessionAsync(CreateUploadSessionRequest request, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UploadSessionSnapshot>(Unavailable());

    public ValueTask<UploadSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UploadSessionSnapshot>(Unavailable());

    public ValueTask<UploadChunkResult> PutChunkAsync(Guid sessionId, long offset, string expectedChunkSha256, Stream body, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UploadChunkResult>(Unavailable());

    public ValueTask<UploadFinalizeResult> FinalizeAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UploadFinalizeResult>(Unavailable());

    public ValueTask<UploadHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new UploadHealth(false, "Unavailable", "unconfigured", _detail));

    private UploadRequestException Unavailable() =>
        new("upload_unavailable", _detail, 503);
}
