using MAM.Application.Catalog;
using MAM.Domain.Assets;

namespace MAM.Infrastructure.Catalog;

public sealed class UnavailableAssetCatalog : IAssetCatalog
{
    private readonly string _detail;

    public UnavailableAssetCatalog(string detail)
    {
        _detail = detail;
    }

    public ValueTask<IReadOnlyList<AssetSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(_detail);

    public ValueTask<AssetSnapshot?> GetAsync(AssetId assetId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(_detail);

    public ValueTask<CatalogMutationResult> CreateAsync(string title, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CatalogMutationResult(CatalogMutationStatus.Unavailable, Error: _detail));

    public ValueTask<CatalogMutationResult> UpdateTitleAsync(AssetId assetId, string title, long expectedVersion, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CatalogMutationResult(CatalogMutationStatus.Unavailable, Error: _detail));

    public ValueTask<CatalogHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CatalogHealth(false, "Unavailable", _detail));
}
