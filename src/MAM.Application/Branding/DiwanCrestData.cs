using System.Security.Cryptography;
using System.Text;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;
    private const string CanonicalChunk03Sha256 = "a8c22447ee0ce7952f66ea4fbc3c3f797dbe0c8f13944ffd49bb580132b4effa";
    private static readonly string[] CanonicalChunk03BlockHashes =
    {
        "ba0c06ff99982098efd7fd33ad47c03e27a914950f70a93c926f4a651718083f",
        "dca79f951bd3d48e303e6770ce25036145e4a592506eb7af56d61fce7e520195",
        "076f99803e42e3af4c63305f4512aca192a18396b4af4110f68a43768706df5b",
        "5a91c518e9ecc2dab6051a04dd6f8151243d05fd8e308a6d27f15ec122338b81",
        "598c9857bcb163e4e4e61604de7d8855cdd107c8641532ac2776684410073e96",
        "2a7bffa0fef35575520a31fb834d70f3322a8cbc64a7e6884ed127d7f33a1f1d",
        "6211d74febb482280a5f29db33a3291383e93750e1a949b026ee324263fb5e3e",
        "7521ecb7efb056e1cd51fa25028969862623e0753d492f905d82199355270760",
        "917bd706620623545527bacedd015ff9b14e30a6596014fdfad4a78ce7fb63bf",
        "556cb51198e6f8dd637b65d1c2783e9ee9961850692b6aea937b0f38b84c295e",
        "7740321473a9f7c9ba6e666d36e61a9a1f89aeb3266f6c883ed2f2662c8945ee",
        "f7b0cc7d48f8ddbd5ab8bb1055b66f4d552dde98a159ba1cd665b3fdebcb5661"
    };

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        var canonicalChunk03 = DiagnoseMissingCanonicalChunk03Character();
        var repairedChunk05 = Chunk05[..6000] + CanonicalChunk05Suffix + Chunk05Gap;
        var repairedChunk08 = Chunk08[..11000] + CanonicalChunk08TailBlock5 + Chunk08[12000..];
        var encoded = Chunk01 + Chunk02 + canonicalChunk03 + Chunk04 + repairedChunk05 + Chunk06 + Chunk07 + repairedChunk08;

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

    private static string DiagnoseMissingCanonicalChunk03Character()
    {
        if (CanonicalChunk03.Length != 11999)
            throw new InvalidOperationException($"Unexpected CanonicalChunk03 diagnostic length: {CanonicalChunk03.Length}.");

        var firstMismatch = -1;
        for (var block = 0; block < CanonicalChunk03BlockHashes.Length; block++)
        {
            var offset = block * 1000;
            var length = Math.Min(1000, CanonicalChunk03.Length - offset);
            if (length <= 0)
            {
                firstMismatch = block;
                break;
            }

            var value = CanonicalChunk03.Substring(offset, length);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value))).ToLowerInvariant();
            if (!string.Equals(hash, CanonicalChunk03BlockHashes[block], StringComparison.Ordinal))
            {
                firstMismatch = block;
                break;
            }
        }

        if (firstMismatch < 0)
            throw new InvalidOperationException("CanonicalChunk03 is one character short but no mismatch block was found.");

        var blockOffset = firstMismatch * 1000;
        var available = CanonicalChunk03.Length - blockOffset;
        if (available < 999)
            throw new InvalidOperationException($"CanonicalChunk03 mismatch block {firstMismatch} has only {available} characters.");

        var currentBlockWithoutMissingCharacter = CanonicalChunk03.Substring(blockOffset, 999);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        for (var position = 0; position <= 999; position++)
        {
            foreach (var candidateCharacter in alphabet)
            {
                var candidateBlock = currentBlockWithoutMissingCharacter.Insert(position, candidateCharacter.ToString());
                var blockHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(candidateBlock))).ToLowerInvariant();
                if (!string.Equals(blockHash, CanonicalChunk03BlockHashes[firstMismatch], StringComparison.Ordinal))
                    continue;

                var globalOffset = blockOffset + position;
                var repaired = CanonicalChunk03.Insert(globalOffset, candidateCharacter.ToString());
                var repairedHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(repaired))).ToLowerInvariant();
                if (string.Equals(repairedHash, CanonicalChunk03Sha256, StringComparison.Ordinal))
                    throw new InvalidOperationException($"CanonicalChunk03 recovery found missing character: offset={globalOffset}; char={candidateCharacter}; block={firstMismatch}.");
            }
        }

        throw new InvalidOperationException($"CanonicalChunk03 missing character could not be recovered in block {firstMismatch}.");
    }
}
