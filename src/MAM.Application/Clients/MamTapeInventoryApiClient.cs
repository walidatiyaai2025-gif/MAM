using System.Net;
using System.Net.Http.Json;
using MAM.Application.Tapes;

namespace MAM.Application.Clients;

public sealed class MamTapeInventoryApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamTapeInventoryApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<TapeInventoryPage> ListAsync(string? query = null, int limit = 250, CancellationToken cancellationToken = default)
    {
        var relative = string.IsNullOrWhiteSpace(query)
            ? $"api/v1/tapes/?limit={Math.Clamp(limit, 1, 500)}"
            : $"api/v1/tapes/search?limit={Math.Clamp(limit, 1, 500)}&query={Uri.EscapeDataString(query.Trim())}";
        using var response = await SendAsync(HttpMethod.Get, relative, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeInventoryPage>(cancellationToken: cancellationToken)
            ?? new TapeInventoryPage(Array.Empty<TapeInventoryItem>(), 0);
    }

    public async Task<IReadOnlyList<TapeFormatItem>> ListFormatsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/tapes/formats/list", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeFormatItem[]>(cancellationToken: cancellationToken) ?? Array.Empty<TapeFormatItem>();
    }

    public async Task<IReadOnlyList<TapeDepartmentItem>> ListDepartmentsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/tapes/departments/list?includeInactive={includeInactive.ToString().ToLowerInvariant()}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeDepartmentItem[]>(cancellationToken: cancellationToken) ?? Array.Empty<TapeDepartmentItem>();
    }

    public async Task<TapeInventoryItem> ResolveAsync(string scannedValue, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/tapes/resolve/{Uri.EscapeDataString(scannedValue)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeInventoryItem>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no tape for the scanned barcode.");
    }

    public async Task<TapeBarcodeDescriptor> GetBarcodeAsync(Guid tapeId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/tapes/{tapeId:D}/barcode", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeBarcodeDescriptor>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no barcode descriptor.");
    }

    public async Task RecordPrintEventAsync(Guid tapeId, TapePrintEventRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/v1/tapes/{tapeId:D}/print-events", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<TapeDepartmentItem> UpsertDepartmentAsync(UpsertTapeDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"api/v1/tapes/departments/{Uri.EscapeDataString(request.Code)}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeDepartmentItem>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no tape department.");
    }

    public async Task<TapeFormatItem> UpsertFormatAsync(UpsertTapeFormatRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"api/v1/tapes/formats/{Uri.EscapeDataString(request.Code)}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeFormatItem>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no tape format.");
    }

    public async Task<TapeInventoryItem> CreateAsync(CreateTapeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/tapes/", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeInventoryItem>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no created tape.");
    }

    public async Task<TapeInventoryItem> UpdateAsync(Guid tapeId, UpdateTapeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"api/v1/tapes/{tapeId:D}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TapeInventoryItem>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned no updated tape.");
    }

    public async Task DeleteAsync(Guid tapeId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"api/v1/tapes/{tapeId:D}?expectedVersion={expectedVersion}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API tape request failed." : detail);
    }
}
