using System.Net;
using System.Net.Http.Json;
using MAM.Application.Protection;

namespace MAM.Application.Clients;

public sealed class MamProtectionApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamProtectionApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<BackupProtectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/protection/summary", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BackupProtectionSummary>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned no protection summary.");
    }

    public async Task<BackupProtectionRecord?> GetAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/protection/assets/{assetId:D}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BackupProtectionRecord>(cancellationToken: cancellationToken);
    }

    public async Task<int> QueueAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/protection/queue", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<QueueResponse>(cancellationToken: cancellationToken);
        return payload?.Queued ?? 0;
    }

    public async Task<int> QueueIntegrityRecheckAsync(int olderThanHours = 24, CancellationToken cancellationToken = default)
    {
        var hours = Math.Clamp(olderThanHours, 0, 24 * 365);
        using var response = await SendAsync(HttpMethod.Post, $"api/v1/protection/integrity/recheck?olderThanHours={hours}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<QueueResponse>(cancellationToken: cancellationToken);
        return payload?.Queued ?? 0;
    }

    public async Task<BackupProtectionHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/protection", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<HealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Protection is null)
            throw new MamApiException(response.StatusCode, "Central API protection health response was invalid.");
        return envelope.Protection;
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

    private sealed record QueueResponse(int Queued);
    private sealed record HealthEnvelope(string Status, BackupProtectionHealth Protection);
}
