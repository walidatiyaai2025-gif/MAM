using System.Net.Http.Json;
using MAM.Application.Uploads;

namespace MAM.Application.Clients;

public sealed class MamUploadApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamUploadApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<UploadSessionSnapshot> CreateSessionAsync(
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Post, "api/v1/uploads/sessions");
        message.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UploadSessionSnapshot>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no upload session.");
    }

    public async Task<UploadSessionSnapshot> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Get, $"api/v1/uploads/sessions/{sessionId:D}");
        using var response = await _http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UploadSessionSnapshot>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no upload session state.");
    }

    public async Task<UploadChunkResult> PutChunkAsync(
        Guid sessionId,
        long offset,
        string chunkSha256,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Put, $"api/v1/uploads/sessions/{sessionId:D}/chunks?offset={offset}");
        message.Headers.TryAddWithoutValidation("X-Chunk-SHA256", chunkSha256);
        message.Content = new StreamContent(body);
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UploadChunkResult>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no chunk receipt.");
    }

    public async Task<UploadFinalizeResult> FinalizeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Post, $"api/v1/uploads/sessions/{sessionId:D}/finalize");
        using var response = await _http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UploadFinalizeResult>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no finalized upload result.");
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
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API upload request failed." : detail);
    }
}
