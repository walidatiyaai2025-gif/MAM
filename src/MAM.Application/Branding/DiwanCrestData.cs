using System.Security.Cryptography;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;
    private const int CanonicalChunk03RecoveryOffset = 9434;

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        if (CanonicalChunk03.Length != 11999)
            throw new InvalidOperationException($"Unexpected CanonicalChunk03 source length: {CanonicalChunk03.Length}.");

        // The recovered source transcription was missing one base64 character. CI diagnostics
        // located the exact owner-source character and offset; final byte length + SHA-256 below
        // remain authoritative and fail closed if any source byte differs.
        var repairedChunk03 = CanonicalChunk03.Insert(CanonicalChunk03RecoveryOffset, "e");
        var repairedChunk05 = Chunk05[..6000] + CanonicalChunk05Suffix + Chunk05Gap;
        var repairedChunk08 = Chunk08[..11000] + CanonicalChunk08TailBlock5 + Chunk08[12000..];
        var encoded = Chunk01 + Chunk02 + repairedChunk03 + Chunk04 + repairedChunk05 + Chunk06 + Chunk07 + repairedChunk08;

        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != ApprovedByteLength)
            throw new InvalidOperationException($"Approved Diwan crest byte length validation failed: {bytes.Length}.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"Approved Diwan crest SHA-256 validation failed: {hash}.");

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
