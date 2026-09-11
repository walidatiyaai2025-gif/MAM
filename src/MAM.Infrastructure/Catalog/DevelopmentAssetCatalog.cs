using MAM.Application.Auditing;
using MAM.Application.Catalog;
using MAM.Domain.Assets;

namespace MAM.Infrastructure.Catalog;

public sealed class DevelopmentAssetCatalog : IAssetCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, MediaAsset> _assets = new();
    private readonly IAuditSink _audit;

    public DevelopmentAssetCatalog(IAuditSink audit)
    {
        _audit = audit;
    }

    public ValueTask<IReadOnlyList<AssetSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<AssetSnapshot> result = _assets.Values
                .OrderByDescending(asset => asset.CreatedAtUtc)
                .Select(ToSnapshot)
                .ToArray();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<AssetSnapshot?> GetAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var result = _assets.TryGetValue(assetId.Value, out var asset) ? ToSnapshot(asset) : null;
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<CatalogMutationResult> CreateAsync(string title, string actorId, CancellationToken cancellationToken = default) =>
        CreateWithIdAsync(AssetId.New(), title, actorId, cancellationToken);

    public async ValueTask<CatalogMutationResult> CreateWithIdAsync(
        AssetId assetId,
        string title,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaAsset asset;
        try
        {
            asset = MediaAsset.Create(assetId, title, DateTimeOffset.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: ex.Message);
        }

        lock (_gate)
        {
            if (_assets.ContainsKey(asset.Id.Value))
            {
                return new CatalogMutationResult(CatalogMutationStatus.Conflict, Error: "The requested asset identity already exists.");
            }
            _assets.Add(asset.Id.Value, asset);
        }

        var snapshot = ToSnapshot(asset);
        await _audit.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            actorId,
            "catalog.asset.created",
            "MediaAsset",
            asset.Id.ToString(),
            "Success",
            $"version={asset.Version}"), cancellationToken);

        return new CatalogMutationResult(CatalogMutationStatus.Created, snapshot);
    }

    public async ValueTask<CatalogMutationResult> UpdateTitleAsync(
        AssetId assetId,
        string title,
        long expectedVersion,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssetSnapshot? snapshot;
        bool updated;

        try
        {
            lock (_gate)
            {
                if (!_assets.TryGetValue(assetId.Value, out var asset))
                {
                    return new CatalogMutationResult(CatalogMutationStatus.NotFound);
                }

                updated = asset.TryRename(title, expectedVersion, DateTimeOffset.UtcNow);
                snapshot = ToSnapshot(asset);
            }
        }
        catch (ArgumentException ex)
        {
            return new CatalogMutationResult(CatalogMutationStatus.Invalid, Error: ex.Message);
        }

        if (!updated)
        {
            return new CatalogMutationResult(
                CatalogMutationStatus.Conflict,
                snapshot,
                "The asset was changed by another request. Refresh and retry with the current version.");
        }

        await _audit.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            actorId,
            "catalog.asset.title-updated",
            "MediaAsset",
            assetId.ToString(),
            "Success",
            $"version={snapshot!.Version}"), cancellationToken);

        return new CatalogMutationResult(CatalogMutationStatus.Updated, snapshot);
    }

    public ValueTask<CatalogHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CatalogHealth(
            true,
            "DevelopmentMemory",
            "Non-production development catalog is available. SQL Server remains required for P02 production acceptance."));
    }

    private static AssetSnapshot ToSnapshot(MediaAsset asset) => new(
        asset.Id.Value,
        asset.Title,
        asset.Lifecycle.ToString(),
        asset.Version,
        asset.CreatedAtUtc,
        asset.UpdatedAtUtc);
}
