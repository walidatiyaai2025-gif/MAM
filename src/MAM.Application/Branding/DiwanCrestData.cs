using System.Security.Cryptography;
using System.Text;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;
    private static readonly string[] ApprovedBase64SegmentSha256 =
    {
        "6bccf950e978d3c6b1433e4d45e5ae0eb34173d21ec5cf90b90c537e96b8de46",
        "4b13bef4cc4585b5519d8e77341d6229ea6de4057322872d572c4184b5cf32df",
        "a8c22447ee0ce7952f66ea4fbc3c3f797dbe0c8f13944ffd49bb580132b4effa",
        "de880a7951f0b95eec6e88fe141b438563737ef0b796c60ebbe0e3a4aed71f3d",
        "fcc7a4fc4b010798dad4b62adc1e3fa6640ab37907446608d84f730e4edc123e",
        "a5fc058d87ac98fc8325da3d6a820c85cbbd463bd9ae0459597e34d60b210e5c",
        "a307a019b53e1d2ce3f4354b97fcc7e94984040f6e114989c8d6ecdb04edc545",
        "6b1f622b1015cf22e0934cbfe6f9ddc2bd1fe7b7f6a2b6df943e3c6a2d55ec74"
    };

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        var encoded = Chunk01 + Chunk02 + Chunk03 + Chunk03Gap + Chunk04 + Chunk05 + Chunk05Gap +
                      Chunk06 + Chunk07 + Chunk08;
        var bytes = Convert.FromBase64String(encoded);

        if (bytes.Length != ApprovedByteLength)
            throw new InvalidOperationException("Approved Diwan crest byte length validation failed.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal))
        {
            var mismatches = new List<int>();
            for (var index = 0; index < ApprovedBase64SegmentSha256.Length; index++)
            {
                var offset = index * 12000;
                var length = Math.Min(12000, encoded.Length - offset);
                if (length <= 0)
                {
                    mismatches.Add(index + 1);
                    continue;
                }

                var segment = encoded.Substring(offset, length);
                var segmentHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(segment))).ToLowerInvariant();
                if (!string.Equals(segmentHash, ApprovedBase64SegmentSha256[index], StringComparison.Ordinal))
                    mismatches.Add(index + 1);
            }

            throw new InvalidOperationException(
                $"Approved Diwan crest SHA-256 validation failed. Base64 segment mismatch: {string.Join(',', mismatches)}.");
        }

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
