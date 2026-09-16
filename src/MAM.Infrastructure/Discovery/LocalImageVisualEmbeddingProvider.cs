using MAM.Application.Discovery;
using SkiaSharp;

namespace MAM.Infrastructure.Discovery;

public sealed class LocalImageVisualEmbeddingProvider : IVisualEmbeddingProvider
{
    private const int Grid = 16;
    private const int VectorDimensions = Grid * Grid * 3;
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff", "image/webp"
    };

    private static bool Disabled => string.Equals(
        Environment.GetEnvironmentVariable("MAM_VISUAL_PROVIDER_DISABLED"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    public VisualProviderHealth Health => Disabled
        ? new(false, "LocalImageGrid", "rgb-grid-16x16", 1, VectorDimensions,
            "Visual embedding provider is disabled by deployment policy.")
        : new(true, "LocalImageGrid", "rgb-grid-16x16", 1, VectorDimensions,
            "Cross-platform local deterministic visual descriptor is ready.");

    public async Task<VisualEmbeddingDescriptor> EmbedAsync(Stream content, string? fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        if (Disabled)
            throw new VisualSearchRequestException("visual_provider_unavailable", "Visual embedding provider is disabled by deployment policy.", 503);
        if (content is null || !content.CanRead)
            throw new VisualSearchRequestException("image_required", "A readable image is required.");

        var normalizedType = NormalizeContentType(contentType);
        if (normalizedType is not null && !SupportedContentTypes.Contains(normalizedType))
            throw new VisualSearchRequestException("image_type_not_supported", "The image type is not supported for visual search.", 415);

        await using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > 16L * 1024 * 1024)
                throw new VisualSearchRequestException("image_too_large", "Visual search images are limited to 16 MB.", 413);
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length == 0)
            throw new VisualSearchRequestException("image_empty", "The image is empty.");
        memory.Position = 0;

        try
        {
            using var codec = SKCodec.Create(memory);
            if (codec is null)
                throw new VisualSearchRequestException("image_decode_failed", "The supplied file is not a valid supported image.", 415);
            var sourceInfo = codec.Info;
            if (sourceInfo.Width < 2 || sourceInfo.Height < 2)
                throw new VisualSearchRequestException("image_dimensions_invalid", "The image dimensions are too small for visual search.");
            if ((long)sourceInfo.Width * sourceInfo.Height > 80_000_000L)
                throw new VisualSearchRequestException("image_dimensions_too_large", "The decoded image dimensions exceed the visual search safety limit.", 413);

            memory.Position = 0;
            using var source = SKBitmap.Decode(memory);
            if (source is null)
                throw new VisualSearchRequestException("image_decode_failed", "The supplied image could not be decoded safely.", 415);

            using var canvasBitmap = new SKBitmap(Grid, Grid, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(canvasBitmap))
            {
                canvas.Clear(SKColors.Black);
                var scale = Math.Min((float)Grid / source.Width, (float)Grid / source.Height);
                var width = Math.Max(1f, source.Width * scale);
                var height = Math.Max(1f, source.Height * scale);
                var left = (Grid - width) / 2f;
                var top = (Grid - height) / 2f;
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(source, new SKRect(left, top, left + width, top + height), paint);
                canvas.Flush();
            }

            var values = new float[VectorDimensions];
            var position = 0;
            for (var y = 0; y < Grid; y++)
            {
                for (var x = 0; x < Grid; x++)
                {
                    var pixel = canvasBitmap.GetPixel(x, y);
                    values[position++] = (pixel.Red / 255f) - 0.5f;
                    values[position++] = (pixel.Green / 255f) - 0.5f;
                    values[position++] = (pixel.Blue / 255f) - 0.5f;
                }
            }

            Normalize(values);
            var health = Health;
            return new VisualEmbeddingDescriptor(health.Provider, health.ModelId, health.ModelVersion, values.Length, values);
        }
        catch (VisualSearchRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or SKException)
        {
            throw new VisualSearchRequestException("image_decode_failed", "The supplied image could not be decoded safely.", 415);
        }
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var semicolon = value.IndexOf(';');
        return (semicolon >= 0 ? value[..semicolon] : value).Trim().ToLowerInvariant();
    }

    private static void Normalize(float[] values)
    {
        double sum = 0;
        for (var i = 0; i < values.Length; i++) sum += values[i] * values[i];
        var magnitude = Math.Sqrt(sum);
        if (magnitude < 1e-8) return;
        for (var i = 0; i < values.Length; i++) values[i] = (float)(values[i] / magnitude);
    }
}
