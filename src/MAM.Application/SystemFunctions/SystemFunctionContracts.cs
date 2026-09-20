namespace MAM.Application.SystemFunctions;

public static class MamSystemFunctionKeys
{
    public const string TapeManagement = "tape.management";
    public const string TapeSearchInContent = "tape.search.in-content";
    public const string TapePrinting = "tape.printing";
    public const string PrintableReports = "reports.printable";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TapeManagement, TapeSearchInContent, TapePrinting, PrintableReports
    };
}

public sealed record SystemFunctionItem(
    string FunctionKey,
    string NameEn,
    string NameAr,
    bool IsEnabled,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);

public sealed record UpdateSystemFunctionRequest(bool IsEnabled, long ExpectedVersion);

public interface ISystemFunctionService
{
    Task<IReadOnlyList<SystemFunctionItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(string functionKey, CancellationToken cancellationToken = default);
    Task<SystemFunctionItem> UpdateAsync(string functionKey, UpdateSystemFunctionRequest request, string actorId, CancellationToken cancellationToken = default);
}

public sealed class SystemFunctionRequestException : Exception
{
    public SystemFunctionRequestException(string code, string message, int statusCode = 400, SystemFunctionItem? current = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Current = current;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public SystemFunctionItem? Current { get; }
}
