using System.Security.Cryptography;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        var repairedChunk05 = Chunk05[..6000] + CanonicalChunk05Suffix + Chunk05Gap;
        var repairedChunk08 = Chunk08[..11000] + CanonicalChunk08TailBlock5 + Chunk08[12000..];
        var encoded = Chunk01 + Chunk02 + CanonicalChunk03 + Chunk04 + repairedChunk05 + Chunk06 + Chunk07 + repairedChunk08;

        var firstPadding = encoded.IndexOf('=');
        if (encoded.Length % 4 != 0 || (firstPadding >= 0 && firstPadding < encoded.Length - 2))
            throw new InvalidOperationException($"Crest Base64 framing invalid: total={encoded.Length}; c1={Chunk01.Length}; c2={Chunk02.Length}; c3={CanonicalChunk03.Length}; c4={Chunk04.Length}; c5raw={Chunk05.Length}; c5suffix={CanonicalChunk05Suffix.Length}; c5gap={Chunk05Gap.Length}; repaired5={repairedChunk05.Length}; c6={Chunk06.Length}; c7={Chunk07.Length}; c8raw={Chunk08.Length}; c8block={CanonicalChunk08TailBlock5.Length}; repaired8={repairedChunk08.Length}; firstPadding={firstPadding}.");

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
