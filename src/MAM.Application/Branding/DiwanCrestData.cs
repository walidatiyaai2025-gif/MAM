using System.Security.Cryptography;
using System.Text;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;

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
            var mismatches = new List<string>();
            CheckPart("Chunk03", Chunk03, "9a9e1371b2d94c50d5ccfa7bac89d6b273307cf15f4703c608ef32e5ba867c95", mismatches);
            CheckPart("Chunk03Gap", Chunk03Gap, "1ce10dd9888452b9d7e48705a0cac7ad74a4d4015a404cef38ae83adb01dce2b", mismatches);
            CheckPart("Chunk05", Chunk05, "c5f4091169118f12e6139c8b643bbc41476a5d2a423362c0eb3d322e2ae53880", mismatches);
            CheckPart("Chunk05Gap", Chunk05Gap, "541c780cab8e76031f93e9c704e5d2456579305c9cc82c1f20ff161d622f242a", mismatches);
            CheckPart("Chunk08Prefix6000", Chunk08[..6000], "cb429619d360f6e590531e0b25ce863c05c07828d6a94a3eeeada2f4c6d087af", mismatches);
            CheckPart("Chunk08Tail", Chunk08[6000..], "6b1f622b1015cf22e0934cbfe6f9ddc2bd1fe7b7f6a2b6df943e3c6a2d55ec74", mismatches);

            throw new InvalidOperationException(
                $"Approved Diwan crest SHA-256 validation failed. Component mismatch: {string.Join(',', mismatches)}.");
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

    private static void CheckPart(string name, string value, string expectedSha256, ICollection<string> mismatches)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value))).ToLowerInvariant();
        if (!string.Equals(hash, expectedSha256, StringComparison.Ordinal))
            mismatches.Add(name);
    }
}
