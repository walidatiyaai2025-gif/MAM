using System.Text.Json;
using MAM.Application.Auditing;

namespace MAM.Application.Administration;

public static class AdminPolicyCategories
{
    public const string Identity = "Identity";
    public const string Metadata = "Metadata";
    public const string Capture = "Capture";
    public const string Processing = "Processing";
    public const string Storage = "Storage";
    public const string Auth = "Auth";
    public const string Retention = "Retention";
    public const string Branding = "Branding";
    public const string Notification = "Notification";
    public const string System = "System";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Identity, Metadata, Capture, Processing, Storage, Auth, Retention, Branding, Notification, System
    };
}

public sealed record AdministrationHealth(bool IsReady, string Provider, string Detail);

public sealed record AdministrationOverview(
    int Policies,
    int EnabledPolicies,
    int Users,
    int DictionaryEntries,
    int RestartRequired,
    DateTimeOffset GeneratedAtUtc);

public sealed record AdminPolicyRecord(
    string PolicyKey,
    string Category,
    string DisplayNameEn,
    string DisplayNameAr,
    JsonElement Payload,
    string? SecretRef,
    long Version,
    bool RequiresRestart,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminPolicyUpdateRequest(
    long ExpectedVersion,
    string Category,
    string DisplayNameEn,
    string DisplayNameAr,
    JsonElement Payload,
    string? SecretRef,
    bool RequiresRestart,
    bool IsEnabled);

public sealed record AdminPolicyValidationResult(bool Valid, IReadOnlyList<string> Errors, bool RequiresRestart);

public sealed record AdminConnectionTestResult(
    bool Success,
    string Code,
    string Detail,
    string? TargetId = null,
    bool SecretReferenceConfigured = false,
    bool SecretReferenceResolvable = false);

public sealed record AdminUserPolicyRecord(
    Guid UserId,
    string UserName,
    string DisplayName,
    string? ExternalSubject,
    bool IsEnabled,
    long Version,
    IReadOnlyList<string> Roles);

public sealed record AdminUserPolicyUpdateRequest(
    long ExpectedVersion,
    string UserName,
    string DisplayName,
    string? ExternalSubject,
    bool IsEnabled,
    IReadOnlyList<string>? Roles);

public sealed record AdminDictionaryEntry(
    string DictionaryKey,
    string EntryKey,
    string LabelEn,
    string LabelAr,
    bool IsEnabled,
    long Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminDictionaryUpdateRequest(
    long ExpectedVersion,
    string LabelEn,
    string LabelAr,
    bool IsEnabled);

public sealed record AdminAuditQuery(
    string? Actor = null,
    string? Action = null,
    string? Outcome = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 100);

public sealed record AdminAuditResult(IReadOnlyList<AuditEvent> Items, int Limit, DateTimeOffset GeneratedAtUtc);

public sealed class AdministrationRequestException : Exception
{
    public AdministrationRequestException(string code, string message, int statusCode, object? current = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Current = current;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public object? Current { get; }
}

public interface IAdministrationService
{
    ValueTask<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    ValueTask<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default);
    ValueTask<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default);
    ValueTask<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default);
    ValueTask<AdminPolicyRecord> UpsertPolicyAsync(string policyKey, AdminPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<AdminConnectionTestResult> TestPolicyAsync(string policyKey, string actorId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default);
    ValueTask<AdminUserPolicyRecord> UpsertUserAsync(Guid userId, AdminUserPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default);
    ValueTask<AdminDictionaryEntry> UpsertDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default);
    ValueTask<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default);
}

public sealed class UnavailableAdministrationService : IAdministrationService
{
    private readonly string _reason;

    public UnavailableAdministrationService(string reason) => _reason = reason;

    public ValueTask<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AdministrationHealth(false, "Unavailable", _reason));

    private AdministrationRequestException Failure() => new("administration_unavailable", _reason, 503);
    public ValueTask<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminPolicyRecord> UpsertPolicyAsync(string policyKey, AdminPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminConnectionTestResult> TestPolicyAsync(string policyKey, string actorId, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminUserPolicyRecord> UpsertUserAsync(Guid userId, AdminUserPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminDictionaryEntry> UpsertDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, string actorId, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default) => throw Failure();
    public ValueTask<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default) => throw Failure();
}
