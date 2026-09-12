using System.Net.Http.Json;
using MAM.Application.Administration;

namespace MAM.Application.Clients;

public sealed class MamAdministrationApiClient
{
    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string? _developmentUser;

    public MamAdministrationApiClient(HttpClient http, string clientName, string? developmentUser = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null) throw new ArgumentException("HttpClient.BaseAddress is required.", nameof(http));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? throw new ArgumentException("Client name is required.", nameof(clientName)) : clientName.Trim();
        _developmentUser = string.IsNullOrWhiteSpace(developmentUser) ? null : developmentUser.Trim();
    }

    public async Task<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "health/administration", null, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<AdministrationHealthEnvelope>(cancellationToken: cancellationToken);
        if (envelope?.Administration is null) throw new MamApiException(response.StatusCode, "Central API administration health response was invalid.");
        return envelope.Administration;
    }

    public Task<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<AdministrationOverview>(HttpMethod.Get, "api/v1/admin/overview", null, cancellationToken);

    public Task<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default) =>
        ReadArrayAsync<AdminPolicyRecord>(HttpMethod.Get, "api/v1/admin/policies", null, cancellationToken);

    public async Task<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/admin/policies/{Uri.EscapeDataString(policyKey)}", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminPolicyRecord>(cancellationToken: cancellationToken);
    }

    public Task<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminPolicyValidationResult>(HttpMethod.Post, $"api/v1/admin/policies/{Uri.EscapeDataString(policyKey)}/validate", request, cancellationToken);

    public Task<AdminPolicyRecord> UpdatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminPolicyRecord>(HttpMethod.Put, $"api/v1/admin/policies/{Uri.EscapeDataString(policyKey)}", request, cancellationToken);

    public Task<AdminConnectionTestResult> TestPolicyAsync(string policyKey, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminConnectionTestResult>(HttpMethod.Post, $"api/v1/admin/policies/{Uri.EscapeDataString(policyKey)}/test", null, cancellationToken);

    public Task<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        ReadArrayAsync<AdminUserPolicyRecord>(HttpMethod.Get, "api/v1/admin/users", null, cancellationToken);

    public Task<AdminUserPolicyRecord> UpdateUserAsync(Guid userId, AdminUserPolicyUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminUserPolicyRecord>(HttpMethod.Put, $"api/v1/admin/users/{userId:D}", request, cancellationToken);

    public Task<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default) =>
        ReadArrayAsync<AdminDictionaryEntry>(HttpMethod.Get, $"api/v1/admin/dictionaries/{Uri.EscapeDataString(dictionaryKey)}", null, cancellationToken);

    public Task<AdminDictionaryEntry> UpdateDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminDictionaryEntry>(HttpMethod.Put, $"api/v1/admin/dictionaries/{Uri.EscapeDataString(dictionaryKey)}/{Uri.EscapeDataString(entryKey)}", request, cancellationToken);

    public Task<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default) =>
        ReadAsync<AdminAuditResult>(HttpMethod.Get, "api/v1/admin/audit" + AuditQuery(query), null, cancellationToken);

    public async Task<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/admin/audit/export" + AuditQuery(query), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new MamApiException(response.StatusCode, "Central API returned an empty administration response.");
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
        try { return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch { request.Dispose(); throw; }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MamApiException(response.StatusCode, string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase ?? "Central API administration request failed." : detail);
    }

    private static string AuditQuery(AdminAuditQuery query)
    {
        query ??= new AdminAuditQuery();
        var items = new List<string>();
        Add(items, "actor", query.Actor);
        Add(items, "action", query.Action);
        Add(items, "outcome", query.Outcome);
        if (query.FromUtc is DateTimeOffset from) items.Add("fromUtc=" + Uri.EscapeDataString(from.ToString("O")));
        if (query.ToUtc is DateTimeOffset to) items.Add("toUtc=" + Uri.EscapeDataString(to.ToString("O")));
        items.Add("limit=" + Math.Clamp(query.Limit, 1, 500));
        return "?" + string.Join('&', items);
    }

    private static void Add(List<string> items, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) items.Add(key + "=" + Uri.EscapeDataString(value.Trim()));
    }

    private sealed record AdministrationHealthEnvelope(string Status, AdministrationHealth Administration);
}
