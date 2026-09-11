using System.Security.Cryptography;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private static readonly Lazy<byte[]> CrestBytes = new(() => Convert.FromBase64String(
        Chunk01 +
        Chunk02 +
        Chunk03 +
        Chunk04 +
        Chunk05 +
        Chunk06 +
        Chunk07 +
        Chunk08));

    public static ReadOnlyMemory<byte> Bytes => CrestBytes.Value;

    public static bool HasApprovedFingerprint()
    {
        var hash = Convert.ToHexString(SHA256.HashData(CrestBytes.Value)).ToLowerInvariant();
        return string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal);
    }
}
