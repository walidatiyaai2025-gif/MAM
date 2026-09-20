namespace MAM.Application.Tapes;

public sealed record TapeDepartmentItem(
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);

public sealed record UpsertTapeDepartmentRequest(
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    int SortOrder);

public sealed record TapePrintEventRequest(
    string Kind,
    string? LabelType,
    decimal? WidthMm,
    decimal? HeightMm);

public sealed record TapeBarcodeDescriptor(
    Guid TapeId,
    string TapeCode,
    string TapeName,
    string Payload);

public static class TapeBarcodePayload
{
    public static TapeBarcodeDescriptor For(TapeInventoryItem tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        var title = string.IsNullOrWhiteSpace(tape.Title) ? "Untitled" : tape.Title.Trim();
        var payload = $"MAM|{tape.TapeCode}|TITLE={Uri.EscapeDataString(title)}";
        return new TapeBarcodeDescriptor(tape.TapeId,tape.TapeCode,title,payload);
    }

    public static string? ExtractTapeCode(string? scannedValue)
    {
        var value = Uri.UnescapeDataString((scannedValue ?? string.Empty).Trim());
        var marker = "TAPE-";
        var start = value.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
        if (start < 0 || value.Length < start + marker.Length + 6) return null;
        var candidate = value.Substring(start,marker.Length+6).ToUpperInvariant();
        return candidate.AsSpan(marker.Length).ToString().All(char.IsDigit) ? candidate : null;
    }
}

public interface ITapeManagementConfigurationService
{
    Task<IReadOnlyList<TapeDepartmentItem>> ListDepartmentsAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<TapeDepartmentItem> UpsertDepartmentAsync(UpsertTapeDepartmentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveDepartmentAsync(string code, CancellationToken cancellationToken = default);
    Task RecordPrintEventAsync(TapeInventoryItem tape, TapePrintEventRequest request, string actorId, CancellationToken cancellationToken = default);
}
