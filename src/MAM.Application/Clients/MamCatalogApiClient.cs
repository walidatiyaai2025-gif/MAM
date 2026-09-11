using System.Net;
using System.Net.Http.Json;
using MAM.Application.Catalog;
using MAM.Application.Metadata;

namespace MAM.Application.Clients;

public sealed class MamCatalogApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamCatalogApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<IReadOnlyList<AssetSnapshot>> ListAssetsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/catalog/assets", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssetSnapshot[]>(cancellationToken: cancellationToken) ?? Array.Empty<AssetSnapshot>();
    }

    public async Task<AssetSnapshot> CreateAssetAsync(string title, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/catalog/assets", new { title }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssetSnapshot>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no created asset.");
    }

    public async Task<AssetSnapshot> UpdateTitleAsync(Guid assetId, string title, long expectedVersion, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"api/v1/catalog/assets/{assetId:D}/title", new { title, expectedVersion }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AssetSnapshot>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no updated asset.");
    }

    public async Task<IReadOnlyList<MetadataSchemaTemplate>> ListMetadataSchemasAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/metadata/schemas", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MetadataSchemaTemplate[]>(cancellationToken: cancellationToken) ?? Array.Empty<MetadataSchemaTemplate>();
    }

    public async Task<CatalogHealth> GetCatalogHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/ready", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<HealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Catalog is null)
            throw new MamApiException(response.StatusCode, "Central API readiness response did not contain catalog health.");
        return envelope.Catalog;
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
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API request failed." : detail);
    }

    private sealed record HealthEnvelope(string Status, CatalogHealth Catalog);
}

public sealed class MamApiException : Exception
{
    public MamApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
