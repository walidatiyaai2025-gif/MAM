using System.Security.Cryptography;
using System.Text;

namespace MAM.Application.Branding;

public static partial class DiwanCrestData
{
    private const int ApprovedByteLength = 69136;

    private static readonly string[] Chunk03BlockHashes =
    {
        "ba0c06ff99982098efd7fd33ad47c03e27a914950f70a93c926f4a651718083f", "dca79f951bd3d48e303e6770ce25036145e4a592506eb7af56d61fce7e520195", "076f99803e42e3af4c63305f4512aca192a18396b4af4110f68a43768706df5b", "5a91c518e9ecc2dab6051a04dd6f8151243d05fd8e308a6d27f15ec122338b81", "598c9857bcb163e4e4e61604de7d8855cdd107c8641532ac2776684410073e96", "2a7bffa0fef35575520a31fb834d70f3322a8cbc64a7e6884ed127d7f33a1f1d", "6211d74febb482280a5f29db33a3291383e93750e1a949b026ee324263fb5e3e", "7521ecb7efb056e1cd51fa25028969862623e0753d492f905d82199355270760", "917bd706620623545527bacedd015ff9b14e30a6596014fdfad4a78ce7fb63bf", "556cb51198e6f8dd637b65d1c2783e9ee9961850692b6aea937b0f38b84c295e", "347146b4222fa61c874e9ddbadd9d1b842ee07e61b0213dc26f01b8d4dbc2be6"
    };

    private static readonly string[] Chunk05BlockHashes =
    {
        "307a99846a79f0e1a2e44ba1c652e93502045633275913d16bc43dd9ef1d978e", "f1ebff5f714befd0febd3336d7e8ff025b70ce620845a51756a597ede183a227", "6a07c4bb8cd3cd685afec62f910304de7645951586c0f327b9c9bded5f4949c0", "7944db08ac889cea1b55a7bd678bb2104c816c36ef10c22a0fc852a92ca983a5", "046bc3028c853e597ec1d74055aee31b2ce361ef8d227a1e236a91be30dd7547", "b14142d8ee40a4b2836be32a251155734dc9e84b8b16faefa672ff8965499026", "7b9566967dd3b12445b939539d9491e24e8d2909c7f45957ece4bed554657968", "5fe70c18c1c174e1f50bb0032fde7c9731500db184710abfecc8d115d56bf2d7", "5dffc29526a6c57334b5a0ddd23ced5e4b3d63cf6326ddedb0902b87981f564a", "982f925d5031989df473f122ffa9a8891570dceed7e94020118366e854469d35", "3a00f948b98daa3f3d2daa41a3c7106d5e530d95f126fc9b6495221de29afec1", "5eac5597cea257622fb38a822cacb78840f64adffba4dd42b46c39bc3dcbffea"
    };

    private static readonly string[] Chunk08TailBlockHashes =
    {
        "cbec056d473854a1f410f446c5db779ff2c12101aea0d1ec2ed6d8c1a9453fc1", "7c4a3353c5bd9ab53a65f45c23ab84bd672669b2f4fdf38392fe352f376c5b01", "51e9491443e6aa1af9b893aa89ff190288a78054f3cc8edc07f089e1bf11929e", "4c7fb28792e0208ded7ae8f9e2e78df1d81b08424b2d93a07c52720143da343b", "80e8135e5ecf858d4456b3d536c6f6f351144d6e5e482ad7af48a45c125882f3", "d588491f164a29716e67cf0f572017d6e23cb644ec7c0d8f3b3bc548bf8f2548", "7349a0ce6bd5c2b73a7b8e23a5c357880f26d15ab3ceff75bcc6f12f0a273b4b", "799422694b8922984811462619ae56d20b5f6968d439f540a2c3d5a95171ddd5", "5cfc21bed0080e14da9013ac506f60ecf691d58a9d4d16f2cd5ac421d0f32dec"
    };

    private static readonly Lazy<byte[]> CrestBytes = new(() =>
    {
        var encoded = Chunk01 + Chunk02 + Chunk03 + Chunk03Gap + Chunk04 + Chunk05 + Chunk05Gap + Chunk06 + Chunk07 + Chunk08;
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != ApprovedByteLength)
            throw new InvalidOperationException("Approved Diwan crest byte length validation failed.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal))
        {
            var mismatches = new List<string>();
            CheckBlocks("Chunk03", Chunk03, Chunk03BlockHashes, mismatches);
            CheckBlocks("Chunk05", Chunk05, Chunk05BlockHashes, mismatches);
            CheckBlocks("Chunk08Tail", Chunk08[6000..], Chunk08TailBlockHashes, mismatches);
            throw new InvalidOperationException($"Approved Diwan crest SHA-256 validation failed. Bad blocks: {string.Join(',', mismatches)}.");
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
        catch { return false; }
    }

    private static void CheckBlocks(string name, string value, IReadOnlyList<string> expectedHashes, ICollection<string> mismatches)
    {
        const int blockSize = 1000;
        for (var index = 0; index < expectedHashes.Count; index++)
        {
            var offset = index * blockSize;
            var length = Math.Min(blockSize, value.Length - offset);
            if (length <= 0)
            {
                mismatches.Add($"{name}[{index}]");
                continue;
            }
            var block = value.Substring(offset, length);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(block))).ToLowerInvariant();
            if (!string.Equals(hash, expectedHashes[index], StringComparison.Ordinal))
                mismatches.Add($"{name}[{index}]");
        }
    }
}
