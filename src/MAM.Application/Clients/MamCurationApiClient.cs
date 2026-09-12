using System.Net.Http.Json;
using MAM.Application.Curation;

namespace MAM.Application.Clients;

public sealed class MamCurationApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamCurationApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<CurationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/curation", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CurationHealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Curation is null)
            throw new MamApiException(response.StatusCode, "Central API curation health response was invalid.");
        return envelope.Curation;
    }

    public async Task<CurationPolicy> GetPolicyAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync<CurationPolicy>(HttpMethod.Get, "api/v1/curation/policy", null, cancellationToken);

    public async Task<CurationSearchResult> SearchAsync(CurationSearchRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new CurationSearchRequest();
        var query = new List<string>();
        Add(query, "query", request.Query);
        Add(query, "lifecycle", request.Lifecycle);
        Add(query, "category", request.Category);
        Add(query, "tag", request.Tag);
        if (request.CollectionId is Guid collectionId) query.Add($"collectionId={Uri.EscapeDataString(collectionId.ToString("D"))}");
        query.Add($"page={Math.Max(1, request.Page)}");
        query.Add($"pageSize={Math.Max(1, request.PageSize)}");
        return await ReadAsync<CurationSearchResult>(HttpMethod.Get, "api/v1/curation/search?" + string.Join('&', query), null, cancellationToken);
    }

    public async Task<AssetMetadataSnapshot?> GetMetadataAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/curation/assets/{assetId:D}/metadata", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssetMetadataSnapshot>(cancellationToken: cancellationToken);
    }

    public Task<AssetMetadataSnapshot> UpdateMetadataAsync(Guid assetId, AssetMetadataUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<AssetMetadataSnapshot>(HttpMethod.Put, $"api/v1/curation/assets/{assetId:D}/metadata", request, cancellationToken);

    public Task<BulkMetadataResult> BulkUpdateMetadataAsync(BulkMetadataRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<BulkMetadataResult>(HttpMethod.Post, "api/v1/curation/assets/bulk-metadata", request, cancellationToken);

    public Task<IReadOnlyList<CollectionSnapshot>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
        ReadArrayAsync<CollectionSnapshot>(HttpMethod.Get, "api/v1/curation/collections", null, cancellationToken);

    public Task<CollectionSnapshot> CreateCollectionAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<CollectionSnapshot>(HttpMethod.Post, "api/v1/curation/collections", request, cancellationToken);

    public Task<CollectionSnapshot> AddToCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, CancellationToken cancellationToken = default) =>
        ReadAsync<CollectionSnapshot>(HttpMethod.Post, $"api/v1/curation/collections/{collectionId:D}/assets/{assetId:D}", new CollectionMembershipRequest(expectedVersion), cancellationToken);

    public Task<CollectionSnapshot> RemoveFromCollectionAsync(Guid collectionId, Guid assetId, long expectedVersion, CancellationToken cancellationToken = default) =>
        ReadAsync<CollectionSnapshot>(HttpMethod.Delete, $"api/v1/curation/collections/{collectionId:D}/assets/{assetId:D}?expectedVersion={expectedVersion}", null, cancellationToken);

    public Task<AssetMetadataSnapshot> ArchiveAsync(Guid assetId, long expectedVersion, CancellationToken cancellationToken = default) =>
        ReadAsync<AssetMetadataSnapshot>(HttpMethod.Post, $"api/v1/curation/assets/{assetId:D}/archive", new LifecycleMutationRequest(expectedVersion), cancellationToken);

    public Task<AssetMetadataSnapshot> RestoreAsync(Guid assetId, long expectedVersion, CancellationToken cancellationToken = default) =>
        ReadAsync<AssetMetadataSnapshot>(HttpMethod.Post, $"api/v1/curation/assets/{assetId:D}/restore", new LifecycleMutationRequest(expectedVersion), cancellationToken);

    private async Task<T> ReadAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned an empty curation response.");
    }

    private async Task<IReadOnlyList<T>> ReadArrayAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T[]>(cancellationToken: cancellationToken) ?? Array.Empty<T>();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.TryAddWithoutValidation("X-MAM-Client", _clientName);
        if (_developmentUser is not null) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", _developmentUser);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API curation request failed." : detail);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) query.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
    }

    private sealed record CurationHealthEnvelope(string Status, CurationHealth Curation);
}
