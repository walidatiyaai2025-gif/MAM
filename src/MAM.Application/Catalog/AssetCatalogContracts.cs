using MAM.Domain.Assets;

namespace MAM.Application.Catalog;

public sealed record AssetSnapshot(
    Guid Id,
    string Title,
    string Lifecycle,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum CatalogMutationStatus
{
    Created,
    Updated,
    NotFound,
    Conflict,
    Invalid,
    Unavailable
}

public sealed record CatalogMutationResult(
    CatalogMutationStatus Status,
    AssetSnapshot? Asset = null,
    string? Error = null);

public sealed record CatalogHealth(
    bool IsReady,
    string Provider,
    string Detail);

public interface IAssetCatalog
{
    ValueTask<IReadOnlyList<AssetSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    ValueTask<AssetSnapshot?> GetAsync(AssetId assetId, CancellationToken cancellationToken = default);
    ValueTask<CatalogMutationResult> CreateAsync(string title, string actorId, CancellationToken cancellationToken = default);
    ValueTask<CatalogMutationResult> UpdateTitleAsync(AssetId assetId, string title, long expectedVersion, string actorId, CancellationToken cancellationToken = default);
    ValueTask<CatalogHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
