using System.Security.Cryptography;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        var bytes = Convert.FromBase64String(
            Chunk01 + Chunk02 + Chunk03 + Chunk03Gap + Chunk04 + Chunk05 + Chunk05Gap +
            Chunk06 + Chunk07 + Chunk08);

        if (bytes.Length != ApprovedByteLength)
            throw new InvalidOperationException("Approved Diwan crest byte length validation failed.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Approved Diwan crest SHA-256 validation failed.");

        return bytes;
    });

    public static ReadOnlyMemory<byte> Bytes => CrestBytes.Value;

    public static bool HasApprovedFingerprint()
    {
        try
        {
            var bytes = Bytes.Span;
            if (bytes.Length != ApprovedByteLength) return false;
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
