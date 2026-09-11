using System.Security.Cryptography;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        // Reconstruct only from owner-approved source bytes. Legacy split files remain for
        // source-history traceability, but known-corrupt portions are replaced by their
        // canonical equivalents recovered from the exact approved PNG.
        var repairedChunk05 = Chunk05[..6000] + CanonicalChunk05Suffix + Chunk05Gap;
        var repairedChunk08 = Chunk08[..11000] + CanonicalChunk08TailBlock5 + Chunk08[12000..];
        var encoded = Chunk01 + Chunk02 + CanonicalChunk03 + Chunk04 + repairedChunk05 + Chunk06 + Chunk07 + repairedChunk08;

        var bytes = Convert.FromBase64String(encoded);
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
