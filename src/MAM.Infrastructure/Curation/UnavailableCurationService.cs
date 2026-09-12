using MAM.Application.Curation;

namespace MAM.Infrastructure.Curation;

public sealed class UnavailableCurationService : ICurationService
{
    private readonly string _reason;

    public UnavailableCurationService(string reason) => _reason = string.IsNullOrWhiteSpace(reason) ? "Authoritative curation service is unavailable." : reason.Trim();

    public CurationPolicy Policy { get; } = new(false, "Saved filters remain disabled without explicit owner/site policy approval.", 100, 100);

    public ValueTask<CurationHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CurationHealth(false, "Unavailable", _reason));

    public ValueTask<CurationSearchResult> SearchAsync(CurationSearchRequest request, CancellationToken cancellationToken = default) => Fail<CurationSearchResult>();
    public ValueTask<AssetMetadataSnapshot?> GetMetadataAsync(Guid assetId, CancellationToken cancellationToken = default) => Fail<AssetMetadataSnapshot?>();
    public ValueTask<AssetMetadataSnapshot> UpdateMetadataAsync(Guid assetId, AssetMetadataUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => Fail<AssetMetadataSnapshot>();
    public ValueTask<BulkMetadataResult> BulkUpdateMetadataAsync(BulkMetadataRequest request, string actorId, CancellationToken cancellationToken = default) => Fail<BulkMetadataResult>();
    public ValueTask<IReadOnlyList<CollectionSnapshot>> ListCollectionsAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<CollectionSnapshot>>();
    public ValueTask<CollectionSnapshot> CreateCollectionAsync(CreateCollectionRequest request, string actorId, CancellationToken cancellationToken = default) => Fail<CollectionSnapshot>();
    public ValueTask<CollectionSnapshot> AddToCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default) => Fail<CollectionSnapshot>();
    public ValueTask<CollectionSnapshot> RemoveFromCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, string actorId, CancellationToken cancellationToken = default) => Fail<CollectionSnapshot>();
    public ValueTask<AssetMetadataSnapshot> SetArchivedAsync(Guid assetId, bool archived, long expectedVersion, string actorId, CancellationToken cancellationToken = default) => Fail<AssetMetadataSnapshot>();

    private ValueTask<T> Fail<T>() => ValueTask.FromException<T>(new CurationRequestException("curation_unavailable", _reason, 503));
}
