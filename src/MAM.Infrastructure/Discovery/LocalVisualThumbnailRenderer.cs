using System.Security.Cryptography;
using MAM.Application.Discovery;
using SkiaSharp;

namespace MAM.Infrastructure.Discovery;

public static class LocalVisualThumbnailRenderer
{
    private const long MaxInputBytes = 64L * 1024 * 1024;
    private const long MaxDecodedPixels = 80_000_000L;
    private const int MaxWidth = 640;
    private const int MaxHeight = 420;

    public static async Task<VisualThumbnailPayload> RenderJpegAsync(Stream content, Guid assetId, CancellationToken cancellationToken = default)
    {
        if (content is null || !content.CanRead)
            throw new VisualSearchRequestException("image_required", "A readable image is required.");

        await using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxInputBytes)
                throw new VisualSearchRequestException("asset_thumbnail_source_too_large", "Image original is too large for the inline visual thumbnail renderer.", 413);
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length == 0)
            throw new VisualSearchRequestException("asset_thumbnail_source_empty", "Image original is empty.", 422);
        memory.Position = 0;

        try
        {
            using var codec = SKCodec.Create(memory);
            if (codec is null)
                throw new VisualSearchRequestException("asset_thumbnail_decode_failed", "Image original could not be decoded for a derived thumbnail.", 415);
            var info = codec.Info;
            if (info.Width < 2 || info.Height < 2 || (long)info.Width * info.Height > MaxDecodedPixels)
                throw new VisualSearchRequestException("asset_thumbnail_dimensions_invalid", "Image dimensions are outside the safe thumbnail rendering range.", 413);

            memory.Position = 0;
            using var source = SKBitmap.Decode(memory);
            if (source is null)
                throw new VisualSearchRequestException("asset_thumbnail_decode_failed", "Image original could not be decoded for a derived thumbnail.", 415);

            var scale = Math.Min(1f, Math.Min((float)MaxWidth / source.Width, (float)MaxHeight / source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var target = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(target))
            {
                canvas.Clear(SKColors.Black);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
                canvas.Flush();
            }
            using var image = SKImage.FromBitmap(target);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 84)
                ?? throw new VisualSearchRequestException("asset_thumbnail_encode_failed", "Derived image thumbnail could not be encoded.", 503);
            var bytes = data.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new VisualThumbnailPayload(new MemoryStream(bytes, writable: false), "image/jpeg", $"visual-{assetId:N}.jpg", bytes.LongLength, sha);
        }
        catch (VisualSearchRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            throw new VisualSearchRequestException("asset_thumbnail_decode_failed", "Image original could not be decoded safely for a derived thumbnail.", 415);
        }
    }
}
