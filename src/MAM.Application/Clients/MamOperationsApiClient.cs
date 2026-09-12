using System.Net.Http.Json;
using MAM.Application.Operations;

namespace MAM.Application.Clients;

public sealed class MamOperationsApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamOperationsApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync("health/operations", cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<OperationsHealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Operations is null) throw new MamApiException(response.StatusCode, "Central API operations health response was invalid.");
        return envelope.Operations;
    }

    public Task<OperationalSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<OperationalSummary>("api/v1/operations/summary", cancellationToken);

    public Task<IngestThroughputReport> GetThroughputAsync(int windowHours = 24, CancellationToken cancellationToken = default) =>
        ReadAsync<IngestThroughputReport>($"api/v1/operations/throughput?windowHours={Math.Clamp(windowHours, 1, 744)}", cancellationToken);

    public Task<DurableQueueReport> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<DurableQueueReport>("api/v1/operations/queues", cancellationToken);

    public Task<IntegrityProtectionReport> GetIntegrityAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IntegrityProtectionReport>("api/v1/operations/integrity", cancellationToken);

    public Task<StorageUsageReport> GetStorageAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<StorageUsageReport>("api/v1/operations/storage", cancellationToken);

    public Task<DependencyHealthReport> GetDependenciesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<DependencyHealthReport>("api/v1/operations/dependencies", cancellationToken);

    public Task<DiagnosticsBundle> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<DiagnosticsBundle>("api/v1/operations/diagnostics", cancellationToken);

    private async Task<T> ReadAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned an empty operations response.");
    }

    private async Task<HttpResponseMessage> SendAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.TryAddWithoutValidation("X-MAM-Client", _clientName);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString("N"));
        if (_developmentUser is not null) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", _developmentUser);
        try { return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch { request.Dispose(); throw; }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API operations request failed." : detail);
    }

    private sealed record OperationsHealthEnvelope(string Status, OperationsHealth Operations);
}
