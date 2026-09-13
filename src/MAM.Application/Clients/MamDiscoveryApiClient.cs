using System.Net;
using System.Net.Http.Json;
using MAM.Application.Discovery;

namespace MAM.Application.Clients;

public sealed class MamDiscoveryApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamDiscoveryApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public Task<DiscoveryDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        GetAsync<DiscoveryDashboardSnapshot>("api/v1/discovery/dashboard", cancellationToken);

    public Task<IReadOnlyList<MediaCapabilitySnapshot>> ListMyMediaCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        GetArrayAsync<MediaCapabilitySnapshot>("api/v1/discovery/my-media-capabilities", cancellationToken);

    public Task<IReadOnlyList<CategorySnapshot>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        GetArrayAsync<CategorySnapshot>("api/v1/discovery/categories", cancellationToken);

    public Task<CategorySnapshot> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
        SendForAsync<CategorySnapshot>(HttpMethod.Post, "api/v1/discovery/categories", request, cancellationToken);

    public Task<CategorySnapshot> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
        SendForAsync<CategorySnapshot>(HttpMethod.Put, $"api/v1/discovery/categories/{categoryId:D}", request, cancellationToken);

    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"api/v1/discovery/categories/{categoryId:D}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AssetCategorySnapshot> GetAssetCategoryAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        GetAsync<AssetCategorySnapshot>($"api/v1/discovery/assets/{assetId:D}/category", cancellationToken);

    public Task<AssetCategorySnapshot> AssignAssetCategoryAsync(Guid assetId, Guid? categoryId, CancellationToken cancellationToken = default) =>
        SendForAsync<AssetCategorySnapshot>(HttpMethod.Put, $"api/v1/discovery/assets/{assetId:D}/category", new AssignAssetCategoryRequest(categoryId), cancellationToken);

    public async Task<AssetTextSnapshot?> GetTextAsync(Guid assetId, string sourceKind, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/discovery/assets/{assetId:D}/text/{Uri.EscapeDataString(sourceKind)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssetTextSnapshot>(cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<TextExtractionStatusSnapshot>> GetExtractionStatusAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        GetArrayAsync<TextExtractionStatusSnapshot>($"api/v1/discovery/assets/{assetId:D}/extraction-status", cancellationToken);

    public Task<DiscoverySearchResult> SearchAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        var values = new List<string>
        {
            $"query={Uri.EscapeDataString(request.Query ?? string.Empty)}",
            $"page={Math.Max(1, request.Page)}",
            $"pageSize={Math.Clamp(request.PageSize, 1, 100)}"
        };
        if (request.CategoryId is Guid categoryId) values.Add($"categoryId={categoryId:D}");
        if (!string.IsNullOrWhiteSpace(request.MediaKind)) values.Add($"mediaKind={Uri.EscapeDataString(request.MediaKind.Trim())}");
        return GetAsync<DiscoverySearchResult>("api/v1/discovery/search?" + string.Join('&', values), cancellationToken);
    }

    public Task<IReadOnlyList<ReferenceSubjectSnapshot>> ListReferenceSubjectsAsync(CancellationToken cancellationToken = default) =>
        GetArrayAsync<ReferenceSubjectSnapshot>("api/v1/discovery/references", cancellationToken);

    public Task<ReferenceSubjectSnapshot> CreateReferenceSubjectAsync(CreateReferenceSubjectRequest request, CancellationToken cancellationToken = default) =>
        SendForAsync<ReferenceSubjectSnapshot>(HttpMethod.Post, "api/v1/discovery/references", request, cancellationToken);

    public Task<ReferenceSubjectSnapshot> AddReferenceImageAsync(Guid subjectId, Guid assetId, CancellationToken cancellationToken = default) =>
        SendForAsync<ReferenceSubjectSnapshot>(HttpMethod.Post, $"api/v1/discovery/references/{subjectId:D}/images", new AddReferenceImageRequest(assetId), cancellationToken);

    public Task<IReadOnlyList<AssetReferenceTagSnapshot>> ListAssetReferenceTagsAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        GetArrayAsync<AssetReferenceTagSnapshot>($"api/v1/discovery/assets/{assetId:D}/reference-tags", cancellationToken);

    public Task<AssetReferenceTagSnapshot> AddAssetReferenceTagAsync(Guid assetId, AddAssetReferenceTagRequest request, CancellationToken cancellationToken = default) =>
        SendForAsync<AssetReferenceTagSnapshot>(HttpMethod.Post, $"api/v1/discovery/assets/{assetId:D}/reference-tags", request, cancellationToken);

    public Task<IReadOnlyList<MediaPermissionSnapshot>> ListMediaPermissionsAsync(CancellationToken cancellationToken = default) =>
        GetArrayAsync<MediaPermissionSnapshot>("api/v1/discovery/media-permissions", cancellationToken);

    public Task<MediaPermissionSnapshot> UpsertMediaPermissionAsync(UpsertMediaPermissionRequest request, CancellationToken cancellationToken = default) =>
        SendForAsync<MediaPermissionSnapshot>(HttpMethod.Put, "api/v1/discovery/media-permissions", request, cancellationToken);

    public async Task<DiscoveryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/discovery", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<DiscoveryHealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Discovery is null)
            throw new MamApiException(response.StatusCode, "Central API discovery health response was invalid.");
        return envelope.Discovery;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned an empty discovery response.");
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T[]>(cancellationToken: cancellationToken) ?? Array.Empty<T>();
    }

    private async Task<T> SendForAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned an empty discovery response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-MAM-Client", _clientName);
        if (_developmentUser is not null) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", _developmentUser);
        if (body is not null) request.Content = JsonContent.Create(body);
        try { return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch { request.Dispose(); throw; }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API request failed." : detail);
    }

    private sealed record DiscoveryHealthEnvelope(string Status, DiscoveryHealth Discovery);
}
