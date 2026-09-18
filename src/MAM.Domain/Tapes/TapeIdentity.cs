namespace MAM.Domain.Tapes;

public readonly record struct TapeId(Guid Value)
{
    public static TapeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public static class TapeCode
{
    public const string Prefix = "TAPE-";
    public const int SequenceDigits = 6;

    public static string FromSequence(long sequence)
    {
        if (sequence <= 0 || sequence > 999999)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Tape sequence must be between 1 and 999999.");
        return $"{Prefix}{sequence.ToString($"D{SequenceDigits}", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}

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
        NotDigitized,
        SentForDigitization,
        PartiallyDigitized,
        DigitizedFileReceived,
        Uploaded,
        QcPending,
        QcApproved,
        Completed
    };
}

public static class TapePhysicalConditions
{
    public const string Good = "Good";
    public const string Fair = "Fair";
    public const string Poor = "Poor";
    public const string Damaged = "Damaged";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Good, Fair, Poor, Damaged
    };
}
