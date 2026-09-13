using MAM.Application.Discovery;

namespace MAM.Infrastructure.Discovery;

public sealed class UnavailableDiscoveryService : IDiscoveryService
{
    private readonly string _detail;
    public UnavailableDiscoveryService(string detail) => _detail = detail;
    private DiscoveryRequestException Unavailable() => new("discovery_unavailable", _detail, 503);

    public Task<DiscoveryHealth> GetHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DiscoveryHealth(false, "Unavailable", _detail));
    public Task<IReadOnlyList<CategorySnapshot>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<CategorySnapshot>>(Unavailable());
    public Task<CategorySnapshot> CreateCategoryAsync(CreateCategoryRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<CategorySnapshot>(Unavailable());
    public Task<CategorySnapshot> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<CategorySnapshot>(Unavailable());
    public Task DeleteCategoryAsync(Guid categoryId, string actorId, CancellationToken cancellationToken = default) => Task.FromException(Unavailable());
    public Task<AssetCategorySnapshot> GetAssetCategoryAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<AssetCategorySnapshot>(Unavailable());
    public Task<AssetCategorySnapshot> AssignAssetCategoryAsync(Guid assetId, Guid? categoryId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<AssetCategorySnapshot>(Unavailable());
    public Task UpsertTextAsync(Guid assetId, string sourceKind, string? language, string text, string? contentSha256, IReadOnlyList<TextSegmentSnapshot>? segments, CancellationToken cancellationToken = default) => Task.FromException(Unavailable());
    public Task<AssetTextSnapshot?> GetTextAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default) => Task.FromException<AssetTextSnapshot?>(Unavailable());
    public Task SetExtractionStatusAsync(Guid assetId, string extractionKind, string state, int progressPercent, string? detail, bool completed, CancellationToken cancellationToken = default) => Task.FromException(Unavailable());
    public Task<IReadOnlyList<TextExtractionStatusSnapshot>> GetExtractionStatusAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<TextExtractionStatusSnapshot>>(Unavailable());
    public Task<DiscoverySearchResult> SearchAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default) => Task.FromException<DiscoverySearchResult>(Unavailable());
    public Task<IReadOnlyList<ReferenceSubjectSnapshot>> ListReferenceSubjectsAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ReferenceSubjectSnapshot>>(Unavailable());
    public Task<ReferenceSubjectSnapshot> CreateReferenceSubjectAsync(CreateReferenceSubjectRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<ReferenceSubjectSnapshot>(Unavailable());
    public Task<ReferenceSubjectSnapshot> AddReferenceImageAsync(Guid subjectId, Guid assetId, string actorId, CancellationToken cancellationToken = default) => Task.FromException<ReferenceSubjectSnapshot>(Unavailable());
    public Task<AssetReferenceTagSnapshot> AddAssetReferenceTagAsync(Guid assetId, AddAssetReferenceTagRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<AssetReferenceTagSnapshot>(Unavailable());
    public Task<IReadOnlyList<AssetReferenceTagSnapshot>> ListAssetReferenceTagsAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<AssetReferenceTagSnapshot>>(Unavailable());
    public Task<IReadOnlyList<MediaPermissionSnapshot>> ListMediaPermissionsAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<MediaPermissionSnapshot>>(Unavailable());
    public Task<MediaPermissionSnapshot> UpsertMediaPermissionAsync(UpsertMediaPermissionRequest request, string actorId, CancellationToken cancellationToken = default) => Task.FromException<MediaPermissionSnapshot>(Unavailable());
    public Task<bool> IsMediaActionAllowedAsync(IEnumerable<string> roles, string mediaKind, string action, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<string> GetAssetMediaKindAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromException<string>(Unavailable());
    public Task<DiscoveryDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default) => Task.FromException<DiscoveryDashboardSnapshot>(Unavailable());
}
