namespace MAM.Application.Tapes;

public sealed record TapeInventoryItem(
    Guid TapeId,
    string TapeCode,
    string? LegacyNumber,
    string? Title,
    string? Description,
    string? TapeFormatCode,
    string? PhysicalCondition,
    string DigitizationStatus,
    string? OwnerDepartment,
    int? DurationSeconds,
    DateOnly? RecordingDate,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? Notes,
    int Version,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);

public sealed record TapeFormatItem(string Code, string NameEn, string NameAr, bool IsActive, int SortOrder);

public sealed record CreateTapeRequest(
    string? LegacyNumber,
    string? Title,
    string? Description,
    string? TapeFormatCode,
    string? PhysicalCondition,
    string? OwnerDepartment,
    int? DurationSeconds,
    DateOnly? RecordingDate,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? Notes);

public sealed record UpdateTapeRequest(
    string? LegacyNumber,
    string? Title,
    string? Description,
    string? TapeFormatCode,
    string? PhysicalCondition,
    string DigitizationStatus,
    string? OwnerDepartment,
    int? DurationSeconds,
    DateOnly? RecordingDate,
    string? Room,
    string? Cabinet,
    string? Shelf,
    string? Bin,
    string? Notes,
    int ExpectedVersion);

public sealed record UpsertTapeFormatRequest(string Code, string NameEn, string NameAr, bool IsActive, int SortOrder);

public sealed record TapeInventoryPage(IReadOnlyList<TapeInventoryItem> Items, int Total);

public interface ITapeInventoryService
{
    Task<TapeInventoryPage> ListAsync(string? query, int limit, CancellationToken cancellationToken = default);
    Task<TapeInventoryItem?> GetAsync(Guid tapeId, CancellationToken cancellationToken = default);
    Task<TapeInventoryItem?> ResolveCodeAsync(string tapeCode, CancellationToken cancellationToken = default);
    Task<TapeInventoryItem> CreateAsync(CreateTapeRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<TapeInventoryItem> UpdateAsync(Guid tapeId, UpdateTapeRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TapeFormatItem>> ListFormatsAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<TapeFormatItem> UpsertFormatAsync(UpsertTapeFormatRequest request, string actorId, CancellationToken cancellationToken = default);
}

public sealed class TapeInventoryRequestException : Exception
{
    public TapeInventoryRequestException(string code, string message, int statusCode = 400, TapeInventoryItem? current = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Current = current;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public TapeInventoryItem? Current { get; }
}
