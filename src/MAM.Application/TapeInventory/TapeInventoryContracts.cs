namespace MAM.Application.TapeInventory;

public static class TapeDigitizationStatuses
{
    public const string NotDigitized = "NotDigitized";
    public const string SentForDigitization = "SentForDigitization";
    public const string PartiallyDigitized = "PartiallyDigitized";
    public const string DigitizedFileReceived = "DigitizedFileReceived";
    public const string Uploaded = "Uploaded";
    public const string QcPending = "QcPending";
    public const string QcApproved = "QcApproved";
    public const string Completed = "Completed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        NotDigitized, SentForDigitization, PartiallyDigitized, DigitizedFileReceived,
        Uploaded, QcPending, QcApproved, Completed
    };
}

public sealed record TapeInventoryRecord(
    Guid TapeId,
    string TapeCode,
    string Title,
    string? Description,
    string? LegacyNumber,
    string? TapeFormat,
    string? PhysicalCondition,
    string DigitizationStatus,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? OwnerDepartment,
    string? Notes,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateTapeRequest(
    string Title,
    string? Description,
    string? LegacyNumber,
    string? TapeFormat,
    string? PhysicalCondition,
    string? DigitizationStatus,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? OwnerDepartment,
    string? Notes);

public sealed record UpdateTapeRequest(
    string Title,
    string? Description,
    string? LegacyNumber,
    string? TapeFormat,
    string? PhysicalCondition,
    string DigitizationStatus,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? OwnerDepartment,
    string? Notes,
    long ExpectedVersion);

public sealed record TapeInventoryHealth(bool IsReady, string Provider, string Detail);

public enum TapeMutationStatus
{
    Created,
    Updated,
    NotFound,
    Conflict,
    Invalid,
    Unavailable
}

public sealed record TapeMutationResult(
    TapeMutationStatus Status,
    TapeInventoryRecord? Tape = null,
    string? Error = null);

public interface ITapeInventoryService
{
    ValueTask<IReadOnlyList<TapeInventoryRecord>> ListAsync(string? query = null, CancellationToken cancellationToken = default);
    ValueTask<TapeInventoryRecord?> GetAsync(Guid tapeId, CancellationToken cancellationToken = default);
    ValueTask<TapeInventoryRecord?> GetByCodeAsync(string tapeCode, CancellationToken cancellationToken = default);
    ValueTask<TapeMutationResult> CreateAsync(CreateTapeRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<TapeMutationResult> UpdateAsync(Guid tapeId, UpdateTapeRequest request, string actorId, CancellationToken cancellationToken = default);
    ValueTask<TapeInventoryHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class UnavailableTapeInventoryService(string detail) : ITapeInventoryService
{
    private readonly string _detail = string.IsNullOrWhiteSpace(detail) ? "Tape inventory is unavailable." : detail;

    public ValueTask<IReadOnlyList<TapeInventoryRecord>> ListAsync(string? query = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IReadOnlyList<TapeInventoryRecord>>(new InvalidOperationException(_detail));
    public ValueTask<TapeInventoryRecord?> GetAsync(Guid tapeId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<TapeInventoryRecord?>(new InvalidOperationException(_detail));
    public ValueTask<TapeInventoryRecord?> GetByCodeAsync(string tapeCode, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<TapeInventoryRecord?>(new InvalidOperationException(_detail));
    public ValueTask<TapeMutationResult> CreateAsync(CreateTapeRequest request, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TapeMutationResult(TapeMutationStatus.Unavailable, Error: _detail));
    public ValueTask<TapeMutationResult> UpdateAsync(Guid tapeId, UpdateTapeRequest request, string actorId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TapeMutationResult(TapeMutationStatus.Unavailable, Error: _detail));
    public ValueTask<TapeInventoryHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TapeInventoryHealth(false, "Unavailable", _detail));
}
