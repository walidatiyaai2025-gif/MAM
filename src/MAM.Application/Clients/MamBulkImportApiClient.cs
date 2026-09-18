using System.Net.Http.Json;
using MAM.Application.BulkImport;

namespace MAM.Application.Clients;

public sealed class MamBulkImportApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamBulkImportApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<BulkImportSessionSnapshot> CreateSessionAsync(CreateBulkImportSessionRequest request, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportSessionSnapshot>(HttpMethod.Post, "api/v1/bulk-import/sessions", request, cancellationToken);

    public async Task<BulkImportSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportSessionSnapshot>(HttpMethod.Get, $"api/v1/bulk-import/sessions/{sessionId:D}", null, cancellationToken);

    public async Task<IReadOnlyList<BulkImportSessionSummary>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        await SendAsync<List<BulkImportSessionSummary>>(HttpMethod.Get, $"api/v1/bulk-import/sessions?limit={Math.Clamp(limit, 1, 100)}", null, cancellationToken);

    public async Task<BulkImportItemSnapshot> BeginItemAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportItemSnapshot>(HttpMethod.Post, $"api/v1/bulk-import/sessions/{sessionId:D}/items/{itemId:D}/begin", null, cancellationToken);

    public async Task<BulkImportItemSnapshot> FinalizeItemAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportItemSnapshot>(HttpMethod.Post, $"api/v1/bulk-import/sessions/{sessionId:D}/items/{itemId:D}/finalize", null, cancellationToken);

    public async Task<BulkImportItemSnapshot> FailItemAsync(Guid sessionId, Guid itemId, string code, string? detail, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportItemSnapshot>(HttpMethod.Post, $"api/v1/bulk-import/sessions/{sessionId:D}/items/{itemId:D}/fail", new BulkImportFailureRequest(code, detail), cancellationToken);

    public async Task<BulkImportItemSnapshot> RetryItemAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportItemSnapshot>(HttpMethod.Post, $"api/v1/bulk-import/sessions/{sessionId:D}/items/{itemId:D}/retry", null, cancellationToken);

    public async Task<BulkImportSessionSnapshot> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<BulkImportSessionSnapshot>(HttpMethod.Post, $"api/v1/bulk-import/sessions/{sessionId:D}/cancel", null, cancellationToken);

    public async Task<(string FileName, string Content)> DownloadReportAsync(Guid sessionId, string format, CancellationToken cancellationToken = default)
    {
        var normalized = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "txt";
        using var request = NewRequest(HttpMethod.Get, $"api/v1/bulk-import/sessions/{sessionId:D}/report/{normalized}");
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? $"MAM-Bulk-Import-{sessionId:D}.{normalized}";
        return (fileName, content);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = NewRequest(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
               ?? throw new MamApiException(response.StatusCode, "Central API returned no bulk import payload.");
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.TryAddWithoutValidation("X-MAM-Client", _clientName);
        if (_developmentUser is not null) request.Headers.TryAddWithoutValidation("X-MAM-Dev-User", _developmentUser);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API bulk import request failed." : detail);
    }
}
