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
    private const string Utf8Prefix = "UTF8=";

    public static TapeBarcodeDescriptor For(TapeInventoryItem tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        var title = string.IsNullOrWhiteSpace(tape.Title) ? "Untitled" : tape.Title.Trim();

        // Printed Code 128 labels carry the durable tape identity only. Keeping the payload
        // to TAPE-###### gives the bars a materially wider X-dimension on small 50–60 mm labels
        // and makes office-printer output much more reliable for handheld scanners. The tape
        // name remains human-readable text on the label. ExtractTapeName still supports older
        // labels that embedded the title so previously printed stock remains resolvable.
        var payload = tape.TapeCode;
        return new TapeBarcodeDescriptor(tape.TapeId, tape.TapeCode, title, payload);
    }

    public static string? ExtractTapeCode(string? scannedValue)
    {
        var value = (scannedValue ?? string.Empty).Trim();
        var marker = "TAPE-";
        var start = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || value.Length < start + marker.Length + 6)
        {
            // Backward compatibility with labels created before the compact T2.2 payload.
            try
            {
                var decoded = Uri.UnescapeDataString(value);
                start = decoded.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0 || decoded.Length < start + marker.Length + 6) return null;
                value = decoded;
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        var candidate = value.Substring(start, marker.Length + 6).ToUpperInvariant();
        return candidate.AsSpan(marker.Length).ToString().All(char.IsDigit) ? candidate : null;
    }

    public static string? ExtractTapeName(string? scannedValue)
    {
        var raw = (scannedValue ?? string.Empty).Trim();
        var code = ExtractTapeCode(raw);
        if (code is null) return null;

        var codeIndex = raw.IndexOf(code, StringComparison.OrdinalIgnoreCase);
        if (codeIndex >= 0)
        {
            var separator = codeIndex + code.Length;
            if (separator < raw.Length && raw[separator] == '|')
            {
                var encoded = raw[(separator + 1)..];
                if (encoded.StartsWith(Utf8Prefix, StringComparison.Ordinal))
                {
                    try
                    {
                        return System.Text.Encoding.UTF8.GetString(Base64UrlDecode(encoded[Utf8Prefix.Length..]));
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }

                if (encoded.StartsWith("TITLE=", StringComparison.OrdinalIgnoreCase))
                {
                    try { return Uri.UnescapeDataString(encoded["TITLE=".Length..]); }
                    catch (UriFormatException) { return null; }
                }

                return encoded;
            }
        }

        // Legacy payload: MAM|TAPE-000001|TITLE=...
        var titleMarker = "|TITLE=";
        var titleIndex = raw.IndexOf(titleMarker, StringComparison.OrdinalIgnoreCase);
        if (titleIndex >= 0)
        {
            try { return Uri.UnescapeDataString(raw[(titleIndex + titleMarker.Length)..]); }
            catch (UriFormatException) { return null; }
        }

        return null;
    }

    private static bool IsCode128Ascii(string value) =>
        value.All(ch => ch is >= (char)32 and <= (char)126);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }
}

public interface ITapeManagementConfigurationService
{
    Task<IReadOnlyList<TapeDepartmentItem>> ListDepartmentsAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<TapeDepartmentItem> UpsertDepartmentAsync(UpsertTapeDepartmentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveDepartmentAsync(string code, CancellationToken cancellationToken = default);
    Task RecordPrintEventAsync(TapeInventoryItem tape, TapePrintEventRequest request, string actorId, CancellationToken cancellationToken = default);
}
