using System.Net;
using System.Net.Http.Json;
using MAM.Application.Processing;

namespace MAM.Application.Clients;

public sealed class MamProcessingApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamProcessingApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public Task<IReadOnlyList<ProcessingProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        GetArrayAsync<ProcessingProfileDescriptor>("api/v1/processing/profiles", cancellationToken);

    public async Task<ProcessingJobSnapshot> EnqueueAsync(Guid assetId, string profileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/v1/processing/assets/{assetId:D}/jobs", new { profileId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProcessingJobSnapshot>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned no processing job.");
    }

    public Task<IReadOnlyList<ProcessingJobSnapshot>> ListJobsAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        GetArrayAsync<ProcessingJobSnapshot>($"api/v1/processing/jobs?limit={Math.Clamp(limit, 1, 500)}", cancellationToken);

    public async Task<ProcessingJobSnapshot> RetryAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/v1/processing/jobs/{jobId:D}/retry", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProcessingJobSnapshot>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned no retried processing job.");
    }

    public async Task<TechnicalMetadataSnapshot?> GetTechnicalAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/processing/assets/{assetId:D}/technical", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TechnicalMetadataSnapshot>(cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<DerivativeSnapshot>> ListDerivativesAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        GetArrayAsync<DerivativeSnapshot>($"api/v1/processing/assets/{assetId:D}/derivatives", cancellationToken);

    public Task<ProcessingDownload> DownloadDerivativeAsync(Guid assetId, Guid derivativeId, CancellationToken cancellationToken = default) =>
        DownloadAsync($"api/v1/processing/assets/{assetId:D}/derivatives/{derivativeId:D}/content", cancellationToken);

    public Task<ProcessingDownload> DownloadOriginalPreviewAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        DownloadAsync($"api/v1/processing/assets/{assetId:D}/preview/original", cancellationToken);

    public async Task<ProcessingHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/processing", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ProcessingHealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Processing is null)
            throw new MamApiException(response.StatusCode, "Central API processing health response was invalid.");
        return envelope.Processing;
    }

    private async Task<ProcessingDownload> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? "preview.bin";
        return new ProcessingDownload(bytes, contentType, fileName);
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T[]>(cancellationToken: cancellationToken) ?? Array.Empty<T>();
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

    private sealed record ProcessingHealthEnvelope(string Status, ProcessingHealth Processing);
}

public sealed record ProcessingDownload(byte[] Content, string ContentType, string FileName);
