using System.Security.Cryptography;
using System.Text.Json;
using MAM.Application.Branding;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MAM.BrandExport <output-directory>");
    return 2;
}

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var png = DiwanCrestData.Bytes.ToArray();
var hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
if (!string.Equals(hash, BrandTokens.CrestSha256, StringComparison.Ordinal))
    throw new InvalidOperationException($"Approved Diwan crest fingerprint mismatch: {hash}.");

var pngPath = Path.Combine(output, "diwan-al-amiri-crest.png");
File.WriteAllBytes(pngPath, png);

var manifest = new
{
    asset = "Diwan Al Amiri approved crest",
    source = "DiwanCrestData.Bytes",
    bytes = png.Length,
    sha256 = hash,
    preserveOriginalColors = true
};
File.WriteAllText(
    Path.Combine(output, "brand-manifest.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

Console.WriteLine(JsonSerializer.Serialize(manifest));
return 0;
